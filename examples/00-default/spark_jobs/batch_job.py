"""Spark job definition: batch aggregation."""
import argparse

parser = argparse.ArgumentParser()
parser.add_argument("--mode", default="full")
parser.add_argument("--epochs", type=int, default=10)
args = parser.parse_args()

print(f"Running batch job: mode={args.mode}, epochs={args.epochs}")
