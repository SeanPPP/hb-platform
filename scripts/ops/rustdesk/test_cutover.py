import unittest

from cutover import rollback_ports


class RollbackPortsTests(unittest.TestCase):
    def test_preserves_tcp_and_udp_bindings(self):
        container = {"HostConfig": {"PortBindings": {
            "21116/tcp": [{"HostIp": "", "HostPort": "21116"}],
            "21116/udp": [{"HostIp": "", "HostPort": "21116"}],
        }}}
        self.assertEqual(rollback_ports(container), [
            {"target": 21116, "published": "21116", "protocol": "tcp"},
            {"target": 21116, "published": "21116", "protocol": "udp"},
        ])

    def test_preserves_custom_host_port_and_interface(self):
        container = {"HostConfig": {"PortBindings": {
            "21116/udp": [
                {"HostIp": "127.0.0.1", "HostPort": "32116"},
                {"HostIp": "::1", "HostPort": "32116"},
            ],
        }}}
        self.assertEqual(rollback_ports(container), [
            {"target": 21116, "published": "32116", "protocol": "udp", "host_ip": "127.0.0.1"},
            {"target": 21116, "published": "32116", "protocol": "udp", "host_ip": "::1"},
        ])

    def test_rejects_missing_or_dynamic_bindings_before_cutover(self):
        for ports in [None, {}, {"21116/udp": [{"HostIp": "", "HostPort": ""}]}]:
            with self.subTest(ports=ports), self.assertRaises(ValueError):
                rollback_ports({"HostConfig": {"PortBindings": ports}})


if __name__ == "__main__":
    unittest.main()
