"""Process telemetry unit checks use real ephemeral local child processes."""
import os
import subprocess
import sys
import time
import unittest

from client.tests.process_telemetry import ProcessTelemetry


class ProcessTelemetryTests(unittest.TestCase):
    def test_real_process_samples_and_finish(self):
        with ProcessTelemetryForTest() as telemetry:
            child = subprocess.Popen([
                sys.executable, "-c",
                "import time; b=bytearray(12*1024*1024); "
                "b[0]=1; sum(range(300000)); time.sleep(0.35)"
            ])
            try:
                telemetry.register("client", child)
                with self.assertRaises(ValueError):
                    telemetry.register("client", child)
                self.assertEqual(child.wait(timeout=10), 0)
                telemetry.finish("client")
            finally:
                if child.poll() is None:
                    child.kill()
                    child.wait()
        result = telemetry.report()["client"]
        self.assertGreater(result["sample_count"], 0)
        self.assertGreater(result["observed_wall_seconds"], 0)
        if os.name == "nt" or os.path.isdir("/proc"):
            self.assertIsNotNone(result["peak_process_rss_bytes"])
            self.assertGreater(result["peak_process_rss_bytes"], 12 * 1024 * 1024)
            self.assertGreaterEqual(result["cpu_seconds"], 0)

    def test_reject_nonpositive_interval(self):
        with self.assertRaises(ValueError):
            ProcessTelemetry(0)


class ProcessTelemetryForTest(ProcessTelemetry):
    def __enter__(self):
        return self

    def __exit__(self, *args):
        self.close()


if __name__ == "__main__":
    unittest.main()
