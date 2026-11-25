# Guia de Publicação Gratuita em Produção — Neon + Render + Vercel / Cloudflare

Este guia usa camadas gratuitas (**free tier**) para colocar o **PITAKO SaaS** online na internet sem custo de servidor inicial:

1. **Neon (PostgreSQL Gerenciado Gratuito)** → Banco de dados PostgreSQL com todas as tabelas e políticas RLS.
2. **Render (Web Service Docker Gratuito)** → Backend `Pitako.Api` (.NET 8 + SignalR + Migrations automáticas).
3. **Vercel (Hobby Gratuito) + Cloudflare (DNS/SSL Gratuito opcional)** → Frontend `pitako-web` (Next.js).

---

## Passo 1 — Criar o banco no Neon (Grátis)

1. Crie uma conta gratuita em **https://neon.tech** e clique em **New Project** (ex.: `pitako-prod`).
2. Na tela inicial do projeto, copie a **Connection String** (`postgresql://...`).
   - A `Pitako.Api` aceita diretamente a URL `postgresql://...` do Neon e aplica todas as 19 schemas, migrations e políticas PostgreSQL RLS automaticamente na primeira inicialização.

---

## Passo 2 — Publicar o Backend `Pitako.Api` no Render (Grátis)

1. Crie uma conta em **https://render.com** conectada ao seu GitHub (`Moises-Warlen/Projeto-Pitako`).
2. Clique em **New + → Blueprint** (ou **Web Service** via `Dockerfile` usando o arquivo `render.yaml` deste repositório).
3. Preencha as variáveis de ambiente obrigatórias no painel do Render:
   - `DATABASE_URL`: cole a string `postgresql://...` copiada do **Neon**.
   - `CORS_ALLOWED_ORIGINS`: coloque a URL do seu frontend (ex.: `https://pitako.vercel.app` ou seu domínio no Cloudflare).
   - `BOOTSTRAP_MASTER_EMAIL`: `master@pitako.com.br` (ou seu e-mail real de dono do SaaS).
   - `BOOTSTRAP_MASTER_PASSWORD`: escolha uma senha forte (mín. 8 caracteres com maiúscula, minúscula e número).
   - `JWT_SIGNING_KEY` e `DATA_ENCRYPTION_PASSPHRASE`: deixe o Render gerar automaticamente (`generateValue: true` no `render.yaml`) ou preencha chaves longas próprias (`>= 32` caracteres).
4. Após o deploy finalizar, teste `https://SEU-SERVICO.onrender.com/health`. Deve responder `status: "Healthy"` e `database: "reachable"`.

---

## Passo 3 — Publicar o Frontend `pitako-web` (Grátis) + Cloudflare

### Opção A — Vercel (Recomendada para Next.js App Router dinâmico — 100% grátis)
1. Acesse **https://vercel.com**, importe o repositório `Moises-Warlen/Projeto-Pitako`.
2. Em **Root Directory**, clique em **Edit** e escolha `src/Web/pitako-web`.
3. Em **Environment Variables**, adicione:
   - `NEXT_PUBLIC_API_URL` = `https://SEU-SERVICO.onrender.com` (sem barra no final).
4. Clique em **Deploy**.
5. Volte no **Render** e confirme que `CORS_ALLOWED_ORIGINS` contém exatamente o endereço HTTPS gerado pela Vercel (ex.: `https://projeto-pitako.vercel.app`).

### Opção B — Cloudflare (DNS, Proxy SSL e Domínio Próprio Grátis)
1. Se você tiver um domínio próprio (`.com.br` ou `.com`), aponte os DNS para o **Cloudflare Free** (**https://dash.cloudflare.com**):
   - Crie registros `CNAME` para o domínio principal (`@` ou `app`) apontando para o frontend (Vercel) e `api` apontando para `SEU-SERVICO.onrender.com`.
   - Ative SSL **Full (Strict)** no Cloudflare e adicione `https://seudominio.com.br` em `CORS_ALLOWED_ORIGINS` no Render.

---

## Notas Importantes da Camada Gratuita
- **Cold Start no Render Free:** serviços gratuitos hibernam após ~15 min sem tráfego; a primeira requisição após hibernação pode levar ~30–50 segundos para acordar.
- **Redis e Hardware Físico:** em camada gratuita sem Redis configurado, `/health` informa `redis: "not_configured"` de forma transparente e o sistema usa limitador local em memória; balança/impressora local rodam via `Pitako.HardwareAgent` instalado no PC da loja.
