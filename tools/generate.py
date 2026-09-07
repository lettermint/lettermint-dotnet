#!/usr/bin/env python3
"""Generate C# models and endpoints from the two API specifications."""
import argparse
import copy
import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]

def name(value):
    result = ''.join(p[:1].upper() + p[1:] for p in re.split(r'[^a-zA-Z0-9]', value) if p)
    return ('Value' + result) if result[:1].isdigit() else result

class Generator:
    def __init__(self, specs):
        self.specs = copy.deepcopy(specs)
        # Both FormRequest classes validate string-valued header and metadata maps.
        if 'sending' in self.specs:
            schemas = self.specs['sending']['components']['schemas']
            for payload in [schemas['SendMailRequest'], schemas['SendBatchMailRequest']['items']]:
                for field in ['headers', 'metadata']:
                    payload['properties'][field] = {'type': 'object', 'additionalProperties': {'type': 'string'}}
        # RouteData.php declares a serialized AttachmentDelivery enum value.
        if 'team' in self.specs:
            settings = self.specs['team']['components']['schemas'].get('RouteData', {}).get('properties', {}).get('settings', {})
            if 'attachment_delivery' in settings.get('properties', {}):
                settings['properties']['attachment_delivery'] = {'$ref': '#/components/schemas/AttachmentDelivery'}
        # MessageController returns a Laravel CursorPaginator through Data::collect.
        # Scramble's CursorPaginatedDataCollection annotation describes a different envelope.
        if 'team' in self.specs:
            team = self.specs['team']
            page_template = team['paths']['/domains']['get']['responses']['200']['content']['application/json']['schema']
            for path, model in [('/messages', 'MessageListData'), ('/messages/{messageId}/events', 'MessageEventData')]:
                page = copy.deepcopy(page_template)
                page['properties']['data']['items'] = {'$ref': '#/components/schemas/' + model}
                team['paths'][path]['get']['responses']['200']['content']['application/json']['schema'] = page
        specs = self.specs
        self.schemas = {}
        self.definitions = {}
        for spec in specs.values():
            for key, schema in spec['components']['schemas'].items():
                if key in self.schemas and self.schemas[key] != schema:
                    raise ValueError('Conflicting schema: ' + key)
                self.schemas[key] = schema

    def type(self, schema, hint):
        if not schema or schema is True:
            return 'JsonElement'
        if '$ref' in schema:
            ref = schema['$ref'].split('/')[-1]
            target = self.schemas[ref]
            if target.get('type') == 'array':
                return self.type(target, ref)
            self.define(ref, target)
            return ref
        options = schema.get('anyOf', schema.get('oneOf'))
        if options:
            choices = [o for o in options if o.get('type') != 'null']
            if len(choices) == 1:
                return self.type(choices[0], hint)
            # The API can return either a cursor page or a bare collection.
            if len(choices) == 2 and {o.get('type') for o in choices} == {'array', 'object'}:
                obj = next(o for o in choices if o.get('type') == 'object')
                arr = next(o for o in choices if o.get('type') == 'array')
                if obj.get('properties', {}).get('data') != arr:
                    return f'JsonUnion<{self.type(obj, hint + "Object")}, {self.type(arr, hint + "Array")}>'
                self.define(hint, obj, collection=True)
                return hint
            if all(o.get('type') == 'object' for o in choices):
                return self.type(self.merge_objects(choices), hint)
            if all(o.get('type') == 'string' for o in choices) and any(not o.get('enum') for o in choices):
                return 'string'
            raise ValueError('Unsupported union: ' + hint)
        kind = schema.get('type', 'object')
        if isinstance(kind, list):
            kinds = [k for k in kind if k != 'null']
            if len(kinds) != 1:
                raise ValueError('Unsupported type union: ' + hint)
            kind = kinds[0]
        if schema.get('enum'):
            self.define(hint, schema)
            return hint
        if kind == 'array':
            return f'List<{self.type(schema["items"], hint + "Item")}>'
        if schema.get('properties'):
            self.define(hint, schema)
            return hint
        if kind == 'object':
            additional = schema.get('additionalProperties', {})
            return f'Dictionary<string, {self.type(additional, hint + "Value")}>'
        return {'string':'string', 'integer':'long', 'number':'double', 'boolean':'bool', 'null':'JsonElement'}[kind]

    def merge_objects(self, choices):
        properties = {}
        for schema in choices:
            for key, value in schema.get('properties', {}).items():
                if key in properties and properties[key] != value:
                    old = properties[key]
                    # String const/enum variants share one wire type.
                    if old.get('type') == value.get('type') == 'string':
                        properties[key] = {'type': 'string'}
                    else:
                        raise ValueError('Conflicting object union property: ' + key)
                else:
                    properties[key] = value
        return {'type': 'object', 'properties': properties}

    def define(self, key, schema, collection=False):
        if key in self.definitions:
            return
        self.definitions[key] = ''
        if schema.get('enum'):
            values = schema['enum']
            lines = [f'[JsonConverter(typeof(WireEnumConverter<{key}>))]', f'public enum {key}', '{']
            for v in values:
                lines += [f'    [EnumMember(Value = {json.dumps(str(v))})]', f'    {name(str(v))},']
        else:
            lines = ([f'[JsonConverter(typeof(CollectionResponseConverter<{key}>))]'] if collection else [])
            lines += [f'public sealed class {key} : ApiModel', '{']
            for prop, value in schema.get('properties', {}).items():
                typ = self.type(value, key + name(prop))
                field = name(prop)
                if field == key:
                    field += 'Value'
                # Null means omitted. A raw JSON payload can send explicit null.
                lines += [f'    [JsonPropertyName({json.dumps(prop)})]', f'    public {typ}? {field} {{ get; set; }}', '']
        lines += ['}', '']
        self.definitions[key] = '\n'.join(lines)

    def generate(self):
        for key, schema in self.schemas.items():
            self.type({'$ref':'#/components/schemas/' + key}, key)
        groups = {}
        manifest = []
        mapping = {'index':'List', 'store':'Create', 'show':'Retrieve', 'destroy':'Delete', 'verifySpecificDnsRecord':'VerifyDnsRecord', 'members.show':'RetrieveMember', 'members.assignment.update':'UpdateMemberAssignment'}
        for surface, spec in self.specs.items():
            for path, item in spec['paths'].items():
                for verb, op in item.items():
                    if verb not in ['get','post','put','patch','delete']:
                        continue
                    operation = op['operationId']
                    prefix, action = operation.split('.', 1) if '.' in operation else {
                        'rescheduleMessage': ('message', 'reschedule'),
                        'cancelScheduledMessage': ('message', 'cancel'),
                        'processInboundMessage': ('message', 'process'),
                    }[operation]
                    group = 'EmailClient' if surface == 'sending' else ('ApiClient' if prefix == 'v1' else name({'domain':'domains','message':'messages','project':'projects','route':'routes','suppression':'suppressions','webhook':'webhooks'}.get(prefix,prefix)) + 'Endpoint')
                    method = mapping.get(action, name(action))
                    if prefix == 'stats': method = 'Retrieve'
                    if surface == 'sending': method = {'sendMail':'Send','sendBatchMail':'SendBatch','ping':'Ping'}[action]
                    params = []
                    for param in item.get('parameters',[]) + op.get('parameters',[]):
                        if param['in'] == 'path':params.append('string ' + param['name'])
                    payload = op.get('requestBody', {}).get('content', {}).get('application/json', {}).get('schema')
                    if payload:params.append(self.type(payload, name(operation) + 'Request') + ' payload')
                    params += ['RequestOptions? options = null', 'CancellationToken cancellationToken = default']
                    response = next(v for k,v in op['responses'].items() if k.startswith('2'))
                    content = response.get('content', {})
                    raw = action == 'ping' or (bool(content) and 'application/json' not in content)
                    res_schema = content.get('application/json', {}).get('schema')
                    variants = [v.get('content', {}).get('application/json', {}).get('schema') for k,v in op['responses'].items() if k.startswith('2')]
                    variants = [v for v in variants if v]
                    if len(variants) > 1:
                        res_schema = {'anyOf': variants}
                    typ = 'string' if raw else (self.type(res_schema, {'v1.sendMail':'SendEmailResponse','v1.sendBatchMail':'SendBatchEmailResponse'}.get(operation,name(operation)+'Response')) if res_schema else None)
                    route = re.sub(r'\{([^}]+)\}', r'{Transport.Segment(\1)}', path)
                    call = f'Transport.{"RawAsync" if raw else "SendAsync" + ("<"+typ+">" if typ else "")}(HttpMethod.{name(verb)}, $"{route}", {"payload" if payload else "null"}, options, cancellationToken)'
                    if action == 'ping':call = '(' + 'await ' + call + '.ConfigureAwait(false)).Trim()'
                    signature = f'    public {"async " if action == "ping" else ""}Task{("<"+typ+">") if typ else ""} {method}Async({", ".join(params)})'
                    source = f'    [ApiOperation("{surface}", "{operation}")]\n' + signature + '\n        => ' + call + ';\n'
                    groups.setdefault(group, []).append(source)
                    if payload:
                        raw_params = [p if not p.endswith(' payload') else 'System.Text.Json.JsonElement payload' for p in params]
                        raw_source = source[source.index('    public '):].replace(', '.join(params), ', '.join(raw_params))
                        groups[group].append(raw_source)
                    manifest.append({'surface':surface,'operationId':operation,'class':group,'method':method+'Async','verb':verb.upper(),'path':path,'raw':raw})
        models = '// <auto-generated />\n#nullable enable\nusing System.Runtime.Serialization;\nusing System.Text.Json;\nusing System.Text.Json.Serialization;\nnamespace Lettermint.Models;\n\n' + '\n'.join(self.definitions[k] for k in sorted(self.definitions))
        endpoints = '// <auto-generated />\n#nullable enable\nusing Lettermint.Models;\nnamespace Lettermint;\n\n'
        for group, methods in groups.items():
            base = ' : IDisposable' if group in ['ApiClient','EmailClient'] else ''
            endpoints += f'public sealed partial class {group}{base}\n{{\n    internal Transport Transport {{ get; }}\n    internal {group}(Transport transport) => Transport = transport;\n'
            if base:endpoints += '    public void Dispose() => Transport.Dispose();\n'
            endpoints += '\n'.join(methods) + '}\n\n'
        endpoints += 'public sealed partial class ApiClient\n{\n'
        for group in groups:
            if group.endswith('Endpoint'):
                endpoints += f'    public {group} {group[:-8]} => new(Transport);\n'
        endpoints += '}\n'
        return {'src/Lettermint/Models.g.cs':models,'src/Lettermint/Endpoints.g.cs':endpoints,'specs/operations.json':json.dumps(manifest,indent=2)+'\n'}

def main():
    parser=argparse.ArgumentParser()
    parser.add_argument('--check',action='store_true')
    parser.add_argument('--spec-dir',type=Path,default=ROOT/'specs')
    args=parser.parse_args()
    specs={k:json.loads((args.spec_dir/(k+'-openapi.json')).read_text()) for k in ['sending','team']}
    for relative, content in Generator(specs).generate().items():
        target=ROOT/relative
        if args.check:
            if not target.exists() or target.read_text()!=content:raise SystemExit('Generated file differs: '+relative)
        else:target.write_text(content)

if __name__=='__main__':main()
