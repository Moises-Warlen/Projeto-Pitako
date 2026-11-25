# Instalação e Estado do Ambiente Local

## 1. Ferramentas Verificadas Nesta Máquina (2026-11-24)

| Componente | Versão / Estado | Caminho / Observação |
|---|---|---|
| Git | `2.47.0.windows.1` | `C:\Program Files\Git\cmd\git.exe` |
| .NET SDK | `8.0.411` (LTS) e `9.0.300` | `C:\Program Files\dotnet\dotnet.exe` (pinado em `8.0.411` via `global.json`) |
| Node.js / npm | `v22.17.1` / `10.9.2` | `C:\Program Files\nodejs\` |
| PostgreSQL | `14.18` (**Running**) | Serviço Windows `postgresql-x64-14` |
| Docker | **Não instalado** | Validação em containers pendente |
| Redis | **Não instalado** | Validação contra Redis real pendente |
| Logo oficial PITAKO | **Não localizada** | Diretório `c:\Users\Moises\Desktop\Pitako` estava vazio |

## 2. Pendências de Ambiente Externo

1. **Logo PITAKO:** Aguardando envio/posicionamento do arquivo oficial pela equipe proprietária.
2. **Docker & Redis:** Quando instalados (Docker Desktop ou Redis/Memurai), validar `docker-compose.yml` e testes de integração contra Redis real.
3. **Balança Toledo / Impressora ESC/POS / APIs Marketplaces / WhatsApp:** Dependem de hardware físico ou credenciais sandbox oficiais nas respectivas fases.
