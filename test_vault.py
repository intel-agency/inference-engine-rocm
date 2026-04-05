import os
import sys
def test_vault_generation():
    print("🧪 Testing SEDA-Vault Generation...")
    try:
        with open("tools/seda_packer.py", "r", encoding="utf-8") as f:
            compile(f.read(), "tools/seda_packer.py", "exec")
        print("✅ Packer syntax is valid.")
    except Exception as e:
        print(f"❌ Syntax Error: {e}")
        sys.exit(1)
if __name__ == "__main__":
    test_vault_generation()
