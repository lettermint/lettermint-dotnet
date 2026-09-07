"""Check model generation and operation coverage."""
import json
import unittest
from pathlib import Path
from generate import Generator, ROOT

class GeneratorTests(unittest.TestCase):
    def setUp(self):
        self.specs = {k:json.loads((ROOT/'specs'/f'{k}-openapi.json').read_text()) for k in ['sending','team']}
        self.generator = Generator(self.specs)

    def test_generated_files_are_current(self):
        for path, content in self.generator.generate().items():
            self.assertEqual((ROOT/path).read_text(), content, path)

    def test_all_spec_operations_are_covered(self):
        files = self.generator.generate()
        actual = {(o['surface'],o['operationId']) for o in json.loads(files['specs/operations.json'])}
        expected = {(s,o['operationId']) for s,spec in self.specs.items() for path in spec['paths'].values() for verb,o in path.items() if verb in ['get','post','put','patch','delete']}
        self.assertEqual(actual,expected)

    def test_typed_enum_reference_and_nullable_union(self):
        self.assertEqual(self.generator.type({'$ref':'#/components/schemas/MessageStatus'},'Status'),'MessageStatus')
        self.assertEqual(self.generator.type({'anyOf':[{'$ref':'#/components/schemas/AttachmentDelivery'},{'type':'null'}]},'Delivery'),'AttachmentDelivery')
        self.assertIn('EnumMember(Value = "hard_bounced")',self.generator.definitions['MessageStatus'])
        self.assertEqual(self.generator.type({'type':['integer','null']},'Count'),'long')

    def test_collection_response_keeps_page_fields(self):
        files=self.generator.generate()
        models=files['src/Lettermint/Models.g.cs']
        self.assertIn('class MessageIndexResponse : ApiModel',models)
        self.assertIn('List<MessageListData>? Data',models)
        self.assertIn('string? NextCursor',models)
        self.assertIn('List<RouteStatisticData>? Statistics',models)
        self.assertIn('AttachmentDelivery? AttachmentDelivery',models)

    def test_inline_models_maps_and_null(self):
        self.assertEqual(self.generator.type({'type':'object','additionalProperties':{'type':'string'}},'Headers'),'Dictionary<string, string>')
        self.assertEqual(self.generator.type({'type':['object','null'],'properties':{'enabled':{'type':'boolean'}}},'Settings'),'Settings')
        self.assertIn('bool? Enabled',self.generator.definitions['Settings'])

    def test_source_routes_match_sdk_operations(self):
        fixture = json.loads((ROOT/'tests/Lettermint.Tests/Fixtures/api-source.json').read_text())
        expected = {(r['method'], r['path']) for r in fixture['routes']}
        generated = json.loads(self.generator.generate()['specs/operations.json'])
        actual = {(r['verb'], r['path']) for r in generated}
        self.assertEqual(actual, expected)
        self.assertEqual(len(expected), 52)

    def test_all_success_response_variants_are_typed(self):
        models = self.generator.generate()['src/Lettermint/Models.g.cs']
        self.assertIn('string? ScheduledAt', models)
        self.assertIn('string? TicketIdentifier', models)
        self.assertIn('double? Confidence', models)

    def test_unsupported_union_fails(self):
        with self.assertRaises(ValueError):
            self.generator.type({'oneOf':[{'type':'string'},{'type':'integer'}]},'Unsupported')

    def test_inputs_remain_independent_and_conflicts_fail(self):
        self.assertTrue(Generator({'sending':self.specs['sending']}).generate())
        self.assertTrue(Generator({'team':self.specs['team']}).generate())
        self.specs['sending']['components']['schemas']['MessageStatus']={'type':'boolean'}
        with self.assertRaises(ValueError):Generator(self.specs)

if __name__=='__main__':unittest.main()
