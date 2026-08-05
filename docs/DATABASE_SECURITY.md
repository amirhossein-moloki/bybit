# Database Security Hardening

This document outlines the security decisions, architectures, and guidelines applied to harden the database and protect sensitive information inside the trading bot system.

---

## 1. API Credential Protection Architecture

We handle external exchange API credentials (e.g., API Keys, Secret Keys) with high confidentiality.

```
  +-------------------------------------------------------------+
  |                      Application Layer                      |
  |                (Does not know cryptography)                 |
  +-------------------------------------------------------------+
                                 |
                                 v
  +-------------------------------------------------------------+
  |              IEncryptionService (Abstractions)              |
  +-------------------------------------------------------------+
                                 |
                                 v
  +-------------------------------------------------------------+
  |            Infrastructure Encryption Service (AES)           |
  |             (Derived from SHA-256 secure key)               |
  +-------------------------------------------------------------+
```

### Encryption Key Derivation & Implementation

- **Algorithm**: Advanced Encryption Standard (AES) symmetric encryption in Cipher Block Chaining (CBC) mode with standard PKCS7 padding.
- **Key Standardization**: To support varying key lengths securely without exceptions, the configured encryption key from `SecuritySettings.EncryptionKey` is passed through `SHA-256` to derive a consistent, cryptographically strong **256-bit (32-byte) key**.
- **Inline IV Protection**: For every encryption operation, a unique, cryptographically random **Initialization Vector (IV)** is generated using a secure random number generator (`Aes.GenerateIV()`). This IV is prefixed directly to the encrypted ciphertext before conversion to Base64. When decrypting, the IV is retrieved from the front of the ciphertext. This guarantees high randomization even if identical secrets are stored.

---

## 2. Access Control & Non-Exposure

API keys and secrets are never returned in plain text or loaded into persistent memory unless explicitly required:
- **No Secret Caching**: Secrets are decrypted strictly on-demand for outbound exchange authentication.
- **Leak Prevention in Logging**: The logging system ensures no API Keys, secret credentials, or passwords are written into logs or trace exceptions.
- **Error/Exception Isolation**: Custom exception handling wraps raw database exceptions (`DatabaseException`, `TransactionException`) to keep connection details, raw SQL syntax, or private configurations hidden from logs and stack traces.

---

## 3. Database Security Review

### Hardening Guidelines for Production PostgreSQL

1. **Strong Password Constraints**: Configure strong randomly-generated credentials for the database superuser and application roles.
2. **Environment Overrides**: Connection strings must be loaded exclusively via environment variables (`DATABASE_CONNECTION`) in production environments and never hardcoded.
3. **Least Privilege Account Principle**: The trading bot worker should connect using a dedicated non-superuser role (e.g., `trading_bot_app`) that has access restricted strictly to necessary schema tables and operations. Do not grant `SUPERUSER` or `DB_OWNER` privileges to the worker execution runtime role.
