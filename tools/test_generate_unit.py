"""Test generator behavior without a stored API specification."""
import copy
import json
import subprocess
import sys
import unittest
from generate import Generator, ROOT


class GeneratorTests(unittest.TestCase):
    def test_report_forwarding_mapping_and_project_response_compatibility(self):
        base = {'components': {'schemas': {'ProjectCreatedData': {'type': 'object', 'properties': {'api_token': {'type': 'string'}}}}}, 'paths': {}}
        mappings = {'getReportForwarding': 'RetrieveReportForwardingAsync', 'updateReportForwarding': 'UpdateReportForwardingAsync', 'deleteReportForwarding': 'DeleteReportForwardingAsync', 'verifyReportForwarding': 'VerifyReportForwardingAsync', 'resendReportForwardingCode': 'ResendReportForwardingCodeAsync', 'project.store': 'CreateAsync'}
        for operation, method in mappings.items():
            base['paths'] = {'/projects/{projectId}/report-forwarding': {'get': {'operationId': operation, 'parameters': [{'in': 'path', 'name': 'projectId'}], 'responses': {'200': {'content': {'application/json': {'schema': {'$ref': '#/components/schemas/ProjectCreatedData'}}}}}}}}
            files = Generator({'example': base}).generate()
            self.assertIn(method, files['src/Lettermint/Endpoints.g.cs'])
            self.assertIn('class ProjectsEndpoint', files['src/Lettermint/Endpoints.g.cs'])
            if operation == 'project.store':
                self.assertIn('Task<ProjectStoreResponse>', files['src/Lettermint/Endpoints.g.cs'])
                self.assertIn('class ProjectStoreResponse', files['src/Lettermint/Models.g.cs'])

    def setUp(self):
        self.specs = {'example': {
            'components': {'schemas': {'Status': {'type': 'string', 'enum': ['hard_bounced']}}},
            'paths': {'/examples/{exampleId}': {'get': {
                'operationId': 'domain.show',
                'parameters': [{'name': 'exampleId', 'in': 'path'}],
                'responses': {
                    '200': {'content': {'application/json': {'schema': {'type': 'object', 'properties': {'id': {'type': 'string'}}}}}},
                    '202': {'content': {'application/json': {'schema': {'type': 'object', 'properties': {'ticket': {'type': 'string'}}}}}},
                },
            }}},
        }}

    def test_deterministic_without_input_mutation(self):
        before = copy.deepcopy(self.specs)
        self.assertEqual(Generator(self.specs).generate(), Generator(self.specs).generate())
        self.assertEqual(self.specs, before)

    def test_operation_mapping_and_response_variants(self):
        files = Generator(self.specs).generate()
        operation, = json.loads(files['specs/operations.json'])
        self.assertEqual((operation['surface'], operation['verb'], operation['path']),
                         ('example', 'GET', '/examples/{exampleId}'))
        self.assertIn('Transport.Segment(exampleId)', files['src/Lettermint/Endpoints.g.cs'])
        self.assertIn('string? Ticket', files['src/Lettermint/Models.g.cs'])

    def test_nullable_enum_and_map(self):
        generator = Generator(self.specs)
        self.assertEqual(generator.type({'anyOf': [{'$ref': '#/components/schemas/Status'}, {'type': 'null'}]}, 'Value'), 'Status')
        self.assertEqual(generator.type({'type': 'object', 'additionalProperties': {'type': 'string'}}, 'Headers'), 'Dictionary<string, string>')
        self.assertIn('EnumMember(Value = "hard_bounced")', generator.definitions['Status'])

    def test_unsupported_union_and_conflicts_fail(self):
        with self.assertRaises(ValueError):
            Generator({}).type({'oneOf': [{'type': 'string'}, {'type': 'integer'}]}, 'Value')
        self.specs['other'] = {'components': {'schemas': {'Status': {'type': 'integer'}}}}
        with self.assertRaises(ValueError):
            Generator(self.specs)

    def test_stored_manifest_matches_source_route_fixtures(self):
        fixture = json.loads((ROOT/'tests/Lettermint.Tests/Fixtures/api-source.json').read_text())
        operations = json.loads((ROOT/'specs/operations.json').read_text())
        self.assertEqual({(r['method'], r['path']) for r in fixture['routes']},
                         {(r['verb'], r['path']) for r in operations})

    def test_external_spec_directory_is_required(self):
        result = subprocess.run([sys.executable, str(ROOT/'tools/generate.py'), '--check'], capture_output=True, text=True)
        self.assertEqual(result.returncode, 2)
        self.assertIn('--spec-dir', result.stderr)
        for surface in ['sending', 'team']:
            self.assertFalse((ROOT/'specs'/f'{surface}-openapi.json').exists())
