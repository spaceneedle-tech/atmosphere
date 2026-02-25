# Microsoft Entra External ID (CIAM) — Receita de Configuração

Guia completo para criar e configurar um tenant Microsoft Entra External ID (CIAM) e integrar com o Atmosphere (ASP.NET + YARP).

---

## Índice

1. [Pré-requisitos](#1-pré-requisitos)
2. [Criar o Tenant CIAM](#2-criar-o-tenant-ciam)
3. [Registrar o App da API (Resource)](#3-registrar-o-app-da-api-resource)
4. [Registrar o App do Cliente (SPA / Frontend)](#4-registrar-o-app-do-cliente-spa--frontend)
5. [Configurar o User Flow (Email OTP)](#5-configurar-o-user-flow-email-otp)
6. [Adicionar SPA ao User Flow](#6-adicionar-spa-ao-user-flow)
7. [Configurar Consent (Admin Grant)](#7-configurar-consent-admin-grant)
8. [Configurar o Atmosphere (ASP.NET)](#8-configurar-o-atmosphere-aspnet)
9. [Configurar o Frontend (MSAL.js)](#9-configurar-o-frontend-msaljs)
10. [Problemas Conhecidos e Soluções](#10-problemas-conhecidos-e-soluções)
11. [Referência de IDs](#11-referência-de-ids)

---

## 1. Pré-requisitos

- Conta Microsoft Azure com permissão para criar tenants
- CLI: `az` (Azure CLI) ou acesso ao portal https://entra.microsoft.com
- Para automação via API: token de acesso com escopo `Directory.ReadWrite.All`

---

## 2. Criar o Tenant CIAM

### Via Portal Azure

1. Acesse https://portal.azure.com
2. Menu > **Microsoft Entra ID** > **Manage tenants** > **+ Create**
3. Selecione **Workforce** ou **External** (para CIAM, selecione **External**)
4. Preencha:
   - **Organization name**: `<nome-do-seu-app>external`
   - **Initial domain**: `<nome-do-seu-app>external` (será `<nome>.onmicrosoft.com`)
   - **Country/Region**: escolha o país
5. Selecione o plano **Free** (ou pago)
6. Clique em **Review + Create** > **Create**

### Resultado

Após criação você terá:
- **Tenant ID**: `xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx`
- **Domain**: `<nome>external.onmicrosoft.com`
- **CIAM Login Endpoint**: `https://<tenant-id>.ciamlogin.com/`

> **Importante:** o endpoint CIAM é `<tenant-id>.ciamlogin.com`, não `login.microsoftonline.com`.

---

## 3. Registrar o App da API (Resource)

Este app representa sua API backend — é o **recurso** que será protegido.

### Via Portal Entra

1. Acesse https://entra.microsoft.com e mude para o tenant CIAM criado
2. **Applications** > **App registrations** > **+ New registration**
3. Preencha:
   - **Name**: `<nome-do-app>-api` (ex: `atmosphere-api`)
   - **Supported account types**: `Accounts in this organizational directory only`
   - **Redirect URI**: deixar em branco (API não faz redirect)
4. Clique em **Register**

### Configurar Expose an API

1. Na app registrada, vá em **Expose an API**
2. **Application ID URI** > **+ Add** > aceite o padrão `api://<client-id>`
3. **+ Add a scope**:
   - **Scope name**: `access_as_user`
   - **Who can consent**: `Admins and users`
   - **Admin consent display name**: `Access as user`
   - **Admin consent description**: `Allows the app to access the API as the signed-in user`
   - **State**: `Enabled`
4. Clique em **Add scope**

### Pré-autorizar o App Cliente

1. Ainda em **Expose an API** > **+ Add a client application**
2. **Client ID**: cole o Client ID do app SPA/frontend (criado na seção 4)
3. Marque o scope `access_as_user`
4. Clique em **Add application**

### Resultado esperado

- **Application ID URI**: `api://<api-client-id>`
- **Scope completo**: `api://<api-client-id>/access_as_user`

### Via Microsoft Graph API (automação)

```bash
# 1. Criar app registration
POST https://graph.microsoft.com/v1.0/applications
{
  "displayName": "atmosphere-api",
  "signInAudience": "AzureADMyOrg"
}
# Retorna: { "id": "<object-id>", "appId": "<client-id>" }

# 2. Definir Application ID URI e scope
POST https://graph.microsoft.com/v1.0/applications/<object-id>
PATCH com:
{
  "identifierUris": ["api://<client-id>"],
  "api": {
    "oauth2PermissionScopes": [{
      "adminConsentDescription": "Allows the app to access the API as the signed-in user",
      "adminConsentDisplayName": "Access as user",
      "id": "<novo-guid>",
      "isEnabled": true,
      "type": "User",
      "userConsentDescription": "Allows the app to access the API on your behalf",
      "userConsentDisplayName": "Access as user",
      "value": "access_as_user"
    }]
  }
}

# 3. Criar service principal
POST https://graph.microsoft.com/v1.0/servicePrincipals
{ "appId": "<api-client-id>" }
```

---

## 4. Registrar o App do Cliente (SPA / Frontend)

Este app representa o frontend que os usuários usam para fazer login.

### Via Portal Entra

1. **Applications** > **App registrations** > **+ New registration**
2. Preencha:
   - **Name**: `<nome-do-app>-spa` (ex: `atmosphere-spa`)
   - **Supported account types**: `Accounts in this organizational directory only`
   - **Redirect URI**: selecione **Single-page application (SPA)** e adicione:
     - `http://localhost:3000/` (desenvolvimento)
     - `https://<seu-dominio-producao>/` (produção)
3. Clique em **Register**

### Adicionar API Permission

1. Na app SPA > **API permissions** > **+ Add a permission**
2. **My APIs** > selecione o app da API (ex: `atmosphere-api`)
3. Marque o scope `access_as_user`
4. Clique em **Add permissions**
5. Clique em **Grant admin consent for <tenant>** > **Yes**

### Resultado esperado

- **Client ID**: `xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx`
- **Redirect URIs**: lista de URIs configuradas
- **Permissions**: `api://<api-id>/access_as_user` (com consent concedido)

### Via Microsoft Graph API (automação)

```bash
# 1. Criar app registration SPA
POST https://graph.microsoft.com/v1.0/applications
{
  "displayName": "atmosphere-spa",
  "signInAudience": "AzureADMyOrg",
  "spa": {
    "redirectUris": ["http://localhost:3000/"]
  },
  "requiredResourceAccess": [{
    "resourceAppId": "<api-client-id>",
    "resourceAccess": [{
      "id": "<scope-id-do-access_as_user>",
      "type": "Scope"
    }]
  }]
}

# 2. Criar service principal do SPA
POST https://graph.microsoft.com/v1.0/servicePrincipals
{ "appId": "<spa-client-id>" }

# 3. Dar admin consent (oauth2PermissionGrant)
POST https://graph.microsoft.com/v1.0/oauth2PermissionGrants
{
  "clientId": "<spa-service-principal-object-id>",
  "consentType": "AllPrincipals",
  "resourceId": "<api-service-principal-object-id>",
  "scope": "access_as_user"
}

# 4. Pré-autorizar SPA no app da API
PATCH https://graph.microsoft.com/v1.0/applications/<api-object-id>
{
  "api": {
    "preAuthorizedApplications": [{
      "appId": "<spa-client-id>",
      "delegatedPermissionIds": ["<scope-id-do-access_as_user>"]
    }]
  }
}
```

---

## 5. Configurar o User Flow (Email OTP)

O user flow define como os usuários fazem login (neste caso, via email com código OTP).

### Via Portal Entra (Recomendado)

1. No tenant CIAM > **External Identities** > **User flows** > **+ New user flow**
2. Selecione **Sign up and sign in**
3. Preencha:
   - **Name**: `signupsignin` (resultado: `B2C_1_signupsignin`)
   - **Identity providers**: marque **Email with OTP**
4. **User attributes**: marque os atributos a coletar no cadastro:
   - `Display Name`
   - `Email Address`
5. Clique em **Create**

### Associar Apps ao User Flow

1. No user flow criado > **Applications** > **+ Add application**
2. Adicione tanto o app da API quanto o app SPA

### Via Microsoft Graph API (automação)

```bash
# 1. Criar user flow
POST https://graph.microsoft.com/beta/identity/authenticationEventsFlows
{
  "@odata.type": "#microsoft.graph.externalUsersSelfServiceSignUpEventsFlow",
  "displayName": "signupsignin",
  "onAuthenticationMethodLoadStart": {
    "@odata.type": "#microsoft.graph.onAuthenticationMethodLoadStartExternalUsersSelfServiceSignUp",
    "identityProviders": [
      { "id": "EmailOtp-OAUTH" }
    ]
  },
  "onInteractiveAuthFlowStart": {
    "@odata.type": "#microsoft.graph.onInteractiveAuthFlowStartExternalUsersSelfServiceSignUp",
    "isSignUpAllowed": true
  },
  "onAttributeCollection": {
    "@odata.type": "#microsoft.graph.onAttributeCollectionExternalUsersSelfServiceSignUp",
    "attributes": [
      { "id": "email" },
      { "id": "displayName" }
    ]
  }
}
# Retorna: { "id": "<flow-id>" }

# 2. Adicionar app SPA ao user flow
POST https://graph.microsoft.com/beta/identity/authenticationEventsFlows/<flow-id>/conditions/applications/includeApplications
{
  "appId": "<spa-client-id>"
}

# 3. Adicionar app API ao user flow (se necessário)
POST https://graph.microsoft.com/beta/identity/authenticationEventsFlows/<flow-id>/conditions/applications/includeApplications
{
  "appId": "<api-client-id>"
}
```

---

## 6. Adicionar SPA ao User Flow

> **Atenção:** Se o app SPA não estiver associado ao user flow, o CIAM retornará erro AADSTS ao tentar fazer login.

A associação é feita conforme descrito na seção 5. Verifique em:
**User flows** > selecione o flow > **Applications** — o app SPA deve aparecer na lista.

---

## 7. Configurar Consent (Admin Grant)

Para que o SPA possa obter tokens com o scope `access_as_user` sem pedir consent individual a cada usuário, é necessário o **Admin Consent**.

### Via Portal

1. App SPA > **API permissions**
2. Clique em **Grant admin consent for <tenant>**
3. Confirme com **Yes**

### Via Graph API

```bash
POST https://graph.microsoft.com/v1.0/oauth2PermissionGrants
{
  "clientId": "<spa-service-principal-object-id>",
  "consentType": "AllPrincipals",
  "resourceId": "<api-service-principal-object-id>",
  "scope": "access_as_user"
}
```

---

## 8. Configurar o Atmosphere (ASP.NET)

### 8.1 Pacotes NuGet Necessários

```xml
<PackageReference Include="Microsoft.AspNetCore.Authentication.JwtBearer" Version="8.0.0" />
<PackageReference Include="Microsoft.Identity.Web" Version="1.25.10" />
```

> **Importante:** Use `JwtBearer` versão **8.0.0** (alinhada com .NET 8). Versões anteriores não têm a propriedade `UseSecurityTokenValidators`.

### 8.2 Program.cs

Adicione o seguinte bloco **após** os outros registros de serviços:

```csharp
if (builder.Configuration.GetSection("Entra").GetChildren().Count() > 0)
{
    shouldAddJwtPolicy = true;

    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddMicrosoftIdentityWebApi(builder.Configuration, "Entra");

    // CIAM usa endpoint v2.0 com issuer diferente do endpoint padrão.
    // O AddMicrosoftIdentityWebApi usa authority sem /v2.0, mas os tokens CIAM
    // são emitidos com issuer que inclui /v2.0. Forçamos o MetadataAddress correto.
    //
    // Problema adicional: Microsoft.Identity.Web 1.x espera JwtSecurityToken (handler antigo),
    // mas .NET 8 usa JsonWebTokenHandler por padrão que produz JsonWebToken.
    // UseSecurityTokenValidators=true força o JwtSecurityTokenHandler antigo.
    var entraConfig = builder.Configuration.GetSection("Entra");
    var ciamAuthority = $"{entraConfig["Instance"]}{entraConfig["TenantId"]}/v2.0";
    builder.Services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
    {
        options.MetadataAddress = $"{ciamAuthority}/.well-known/openid-configuration";
        options.Authority = ciamAuthority;
        options.UseSecurityTokenValidators = true;
        options.TokenValidationParameters.ValidIssuers = new[] { ciamAuthority };
        options.TokenValidationParameters.IssuerValidator = (issuer, token, parameters) =>
        {
            if (parameters.ValidIssuers != null && parameters.ValidIssuers.Contains(issuer))
                return issuer;
            throw new Microsoft.IdentityModel.Tokens.SecurityTokenInvalidIssuerException(
                $"Issuer '{issuer}' inválido. Esperado: {ciamAuthority}");
        };
    });
}
```

### 8.3 appsettings.json (ou appsettings.Development.json)

```json
{
  "Entra": {
    "Instance": "https://<tenant-id>.ciamlogin.com/",
    "TenantId": "<tenant-id>",
    "ClientId": "<api-client-id>"
  }
}
```

| Campo | Descrição | Exemplo |
|-------|-----------|---------|
| `Instance` | URL base do tenant CIAM (com barra no final) | `https://abc123.ciamlogin.com/` |
| `TenantId` | GUID do tenant | `6cf7dc78-e8b8-4c08-a573-098c71c4e2ee` |
| `ClientId` | Client ID do **app da API** (não do SPA) | `a3377294-a8ec-411c-807b-9dd25e7c24cd` |

> **Não use** `login.microsoftonline.com` como Instance para tenants CIAM. O endpoint correto é `<tenant-id>.ciamlogin.com`.

### 8.4 Por que o PostConfigure é necessário

O `AddMicrosoftIdentityWebApi` foi projetado para AAD regular e tem dois problemas com CIAM:

**Problema 1 — Issuer mismatch:**
- CIAM emite tokens com issuer `https://<tenant>.ciamlogin.com/<tenant>/v2.0`
- `AddMicrosoftIdentityWebApi` usa OIDC discovery sem `/v2.0`, que retorna issuer sem `/v2.0`
- O `AadIssuerValidator` rejeita o issuer correto do CIAM

**Problema 2 — IDW10403 (Token is not a JWT token):**
- .NET 8 usa `JsonWebTokenHandler` por padrão → produz `JsonWebToken`
- `Microsoft.Identity.Web 1.x` no `OnTokenValidated` faz cast para `JwtSecurityToken` → null → exception
- Solução: `UseSecurityTokenValidators = true` → usa `JwtSecurityTokenHandler` (antigo) → produz `JwtSecurityToken`

---

## 9. Configurar o Frontend (MSAL.js)

### 9.1 Instalação

```html
<script src="https://alcdn.msauth.net/browser/2.38.0/js/msal-browser.min.js"></script>
```

Ou via npm:
```bash
npm install @azure/msal-browser
```

### 9.2 Configuração MSAL

```javascript
const msalConfig = {
    auth: {
        clientId: "<spa-client-id>",
        authority: "https://<tenant-id>.ciamlogin.com/<tenant-id>",
        redirectUri: "http://localhost:3000/",
        knownAuthorities: ["<tenant-id>.ciamlogin.com"]
    },
    cache: {
        cacheLocation: "sessionStorage",
        storeAuthStateInCookie: false
    }
};

const msalInstance = new msal.PublicClientApplication(msalConfig);

// Scopes para login (identidade do usuário)
const loginRequest = {
    scopes: ["openid", "profile", "email", "offline_access", "api://<api-client-id>/access_as_user"]
};

// Scopes para obter token de acesso à API
const apiTokenRequest = {
    scopes: ["api://<api-client-id>/access_as_user"]
};
```

### 9.3 Fluxo de Login (Redirect)

```javascript
// Inicializar e processar redirect de volta do CIAM
async function initializeMsal() {
    await msalInstance.initialize();
    const response = await msalInstance.handleRedirectPromise();
    if (response) {
        msalInstance.setActiveAccount(response.account);
    }
}

// Disparar login
async function login() {
    await msalInstance.loginRedirect(loginRequest);
}

// Obter token para chamar a API
async function getAccessToken() {
    const account = msalInstance.getActiveAccount();
    if (!account) return null;

    try {
        const response = await msalInstance.acquireTokenSilent({
            ...apiTokenRequest,
            account
        });
        return response.accessToken;
    } catch (error) {
        if (error instanceof msal.InteractionRequiredAuthError) {
            await msalInstance.acquireTokenRedirect({ ...apiTokenRequest, account });
        }
        return null;
    }
}

// Chamar API com token
async function callApi(endpoint) {
    const token = await getAccessToken();
    const response = await fetch(`http://localhost:5100${endpoint}`, {
        headers: { "Authorization": `Bearer ${token}` }
    });
    return response.json();
}
```

### 9.4 Parâmetros críticos do MSAL para CIAM

| Parâmetro | Valor | Observação |
|-----------|-------|-----------|
| `authority` | `https://<tenant-id>.ciamlogin.com/<tenant-id>` | **Sem** `/v2.0` no final |
| `knownAuthorities` | `["<tenant-id>.ciamlogin.com"]` | Necessário para CIAM customizado |
| `scopes no login` | incluir o scope da API (`api://...`) | CIAM requer que o scope da API esteja no `loginRequest` |

---

## 10. Problemas Conhecidos e Soluções

### AADSTS500207: The account type can't be used for the resource

**Causa:** Conta Microsoft pessoal (live.com, hotmail.com) tentando autenticar em um app com `signInAudience: AzureADandPersonalMicrosoftAccount`.

**Solução:** Altere o `signInAudience` de AMBOS os apps (API e SPA) para `AzureADMyOrg`:

```bash
PATCH https://graph.microsoft.com/v1.0/applications/<object-id>
{ "signInAudience": "AzureADMyOrg" }
```

### IDW10403: Token is not a JWT token

**Causa:** Incompatibilidade entre `Microsoft.Identity.Web 1.x` e `.NET 8`.
- .NET 8 usa `JsonWebTokenHandler` (padrão) → produz `JsonWebToken`
- Microsoft.Identity.Web 1.x espera `JwtSecurityToken`

**Solução:** No `PostConfigure<JwtBearerOptions>`:
```csharp
options.UseSecurityTokenValidators = true;
```

### IDX10205 / Issuer inválido (401 após login bem-sucedido)

**Causa:** O `AadIssuerValidator` não reconhece o formato de issuer do CIAM (`ciamlogin.com/...`).

**Solução:** Substituir o validator no PostConfigure:
```csharp
options.TokenValidationParameters.ValidIssuers = new[] { ciamAuthority };
options.TokenValidationParameters.IssuerValidator = (issuer, token, parameters) => {
    if (parameters.ValidIssuers?.Contains(issuer) == true) return issuer;
    throw new SecurityTokenInvalidIssuerException($"Issuer '{issuer}' inválido");
};
```

### acquireTokenSilent retorna null silenciosamente

**Causa:** O app SPA não está associado ao User Flow do CIAM.

**Solução:** Adicionar o SPA ao user flow:
```bash
POST https://graph.microsoft.com/beta/identity/authenticationEventsFlows/<flow-id>/conditions/applications/includeApplications
{ "appId": "<spa-client-id>" }
```

### CORS bloqueando chamadas do frontend

**Causa:** O Atmosphere não tem CORS configurado para o domínio do frontend.

**Solução:** Adicionar em `Program.cs`:
```csharp
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
        policy
            .WithOrigins("http://localhost:3000", "https://<seu-dominio>")
            .AllowAnyHeader()
            .AllowAnyMethod());
});

// Antes de UseRouting:
app.UseCors("AllowFrontend");
```

---

## 11. Referência de IDs

### Tenant de Referência (Ambiente de Dev — `dimitrowexternal`)

| Item | Valor |
|------|-------|
| Tenant ID | `6cf7dc78-e8b8-4c08-a573-098c71c4e2ee` |
| Domain | `dimitrowexternal.onmicrosoft.com` |
| CIAM Instance | `https://6cf7dc78-e8b8-4c08-a573-098c71c4e2ee.ciamlogin.com/` |
| API App (ClientId) | `a3377294-a8ec-411c-807b-9dd25e7c24cd` |
| SPA App (ClientId) | `5899f5e0-92e5-422b-a5fd-e9bdd92ed39a` |
| Scope | `api://a3377294-a8ec-411c-807b-9dd25e7c24cd/access_as_user` |
| User Flow ID | `2ba21f2a-85fa-475a-b6bb-f76764e90357` |
| signInAudience | `AzureADMyOrg` |
| Usuário de teste | `murilo.curti@live.com` |

### Tenant Atmosphere (Ambiente de Dev — Smart Award)

| Item | Valor |
|------|-------|
| Tenant ID | `1dcfc631-1370-4d09-839a-f5d6b1f93557` |
| CIAM Instance | `https://1dcfc631-1370-4d09-839a-f5d6b1f93557.ciamlogin.com/` |
| API App (ClientId) | `9a23c1a7-4f2e-4599-ab0b-32df79d41453` |

---

## Checklist — Novo Tenant

Para replicar o setup em um novo tenant ou app, siga esta checklist:

### Tenant & Apps

- [ ] Criar tenant External ID (CIAM)
- [ ] Registrar app da API (`signInAudience: AzureADMyOrg`)
- [ ] Configurar Expose an API com scope `access_as_user`
- [ ] Registrar app SPA com redirect URI(s) corretas (`signInAudience: AzureADMyOrg`)
- [ ] Adicionar API permission no SPA (`access_as_user`)
- [ ] Pré-autorizar o SPA no app da API
- [ ] Dar Admin Consent no tenant

### User Flow

- [ ] Criar user flow "Sign up and sign in" com Email OTP
- [ ] Adicionar app SPA ao user flow
- [ ] (Opcional) Adicionar app API ao user flow

### Atmosphere (ASP.NET)

- [ ] `JwtBearer` versão `8.0.0` no `.csproj`
- [ ] `Microsoft.Identity.Web` versão `1.25.10` no `.csproj`
- [ ] Bloco `Entra` no `appsettings.json` com `Instance`, `TenantId`, `ClientId` (da API)
- [ ] `PostConfigure<JwtBearerOptions>` com `UseSecurityTokenValidators = true` e `IssuerValidator` customizado

### Frontend (MSAL.js)

- [ ] `clientId` = Client ID do SPA
- [ ] `authority` = `https://<tenant-id>.ciamlogin.com/<tenant-id>`
- [ ] `knownAuthorities` inclui `<tenant-id>.ciamlogin.com`
- [ ] `loginRequest.scopes` inclui `api://<api-id>/access_as_user`
- [ ] CORS habilitado no Atmosphere para o domínio do frontend
