"""Compatibility entry point: current BLE flow uses automatic shop eligibility."""
import pathlib, runpy
runpy.run_path(str(pathlib.Path(__file__).with_name("ble-shop-http-smoke.py")), run_name="__main__")
