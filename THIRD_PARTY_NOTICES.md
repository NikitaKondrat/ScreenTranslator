# Local translation dependencies

The model and native runtime are downloaded separately into the user's local application data. They are not included in this Git repository.

## Hy-MT2-1.8B Q4_K_M

- Publisher: Tencent.
- Repository: https://huggingface.co/tencent/Hy-MT2-1.8B-GGUF
- Pinned revision: `a0c709d9fac510f2c807aa3af52872340dc37a4a`.
- File: `Hy-MT2-1.8B-Q4_K_M.gguf` (1,133,080,448 bytes).
- SHA-256: `dc5f44fcf1fa496ee7ad725982c0c8c553a4de00259b53af84c4b89fb0c06699`.
- License: Apache License 2.0; see `Assets/Licenses/Hy-MT2-LICENSE.txt` and the upstream model repository.

## llama.cpp

- Project: https://github.com/ggml-org/llama.cpp
- Pinned release: `b11669`.
- Archive: `llama-b11669-bin-win-cpu-x64.zip`.
- SHA-256: `37b3efcf962847ca869745fd4ccf7f5e7e8dacfeff9fa3523b4ca8cdc66d632b`.
- License: MIT; see `Assets/Licenses/llama.cpp-LICENSE.txt` and the upstream repository for component notices.

After one-time setup, translation uses the local model only. Runtime downloads require an Internet connection.
