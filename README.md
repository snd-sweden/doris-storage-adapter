# DORIS Storage Adapter

Documentation to be done.

## Flow diagrams

```mermaid
---
title: Uploading file and publishing dataset
---
flowchart TD
    Re[/"👤 Researcher"\]
    Rv[/"👤 Reviewer"\]
    D[Doris]
    SA[Storage Adapter]
    SAS@{ shape: lin-cyl, label: "Storage" }
    RD[researchdata.se]

    Re<-->|"1. Request write token"|D
    Re-->|"2. Send file and token"|SA
    SA-->|"3. Store file and checksum"|SAS
    Re-->|"4. Submit for publishing"|Rv
    Rv-->|"5. Publish"|D
    D-->|"6. Publish"|SA
    SA-->|"7. Mark as published"|SAS
    D-->|"8. Publish metadata"|RD
```
```mermaid
---
title: Downloading public file
---
flowchart TD
    RDUser[/"👤 User"\]
    SA[Storage Adapter]
    SAS@{ shape: lin-cyl, label: "Storage" }
    RD[researchdata.se]

    RDUser-->|"1. Request file"|RD
    RD-->|"2. Redirect"|SA
    SA-->|"3. Check if file is published and public"|SAS
    SA-->|"4. Yes, request file"|SAS
    SAS-->|"5. Send file"|SA
    SA-->|"6. Send file"|RDUser
```
```mermaid
---
title: Authorization flow for storing file
---
sequenceDiagram
    actor R as Researcher
    participant D as Doris
    participant SA as Storage adapter
    participant S as Storage (e.g. S3)
    R->>D: Request write token<br/>for dataset version
    break not authorized
        D-->>R: Denied
    end
    D-->>R: Return signed token
    R->>SA: Send file data and token
    D-->>SA: Fetch public key from<br/>https://doris.snd.se/.well-known/jwks.json
    SA->>SA: Validate token signature, audience,<br/>issuer and claims
    break invalid token
        SA-->>R: Denied
    end
    SA->>S: Store file
    S-->>SA: File metadata
    SA-->>R: Success, return file metadata
```
```mermaid
---
title: Authorization flow for publishing dataset version
---
sequenceDiagram
    actor R as Researcher
    participant D as Doris
    participant SA as Storage adapter
    participant S as Storage (e.g. S3)
    R->>+D: Publish dataset version
    D->>D: Generate signed service token
    D->>+SA: Publish dataset version<br/>with service token
    D-->>SA: Fetch public key from<br/>https://doris.snd.se/.well-known/jwks.json
    SA->>SA: Validate token signature, audience,<br/>issuer and claims
    break Invalid token
        SA-->>D: Denied
    end
    SA->>S: Store metadata<br/>(published, open or restricted)
    SA-->>-D: Return success
    D-->>-R: Return success
```
```mermaid
---
title: Authorization flow for reading restricted or not yet published file
---
sequenceDiagram
    actor R as Researcher
    participant D as Doris
    participant SA as Storage adapter
    participant S as Storage (e.g. S3)
    R->>+D: Request read token<br/>for dataset version
    break Not authorized
        D-->>R: Denied
    end
    D-->>-R: Return signed token
    R->>+SA: Request file data using token
    D-->>SA: Fetch public key from<br/>https://doris.snd.se/.well-known/jwks.json
    SA->>SA: Validate token signature, audience,<br/>issuer and claims
    break Invalid token
        SA-->>R: Denied
    end
    SA->>+S: Request file data
    S-->>-SA: Return file data
    SA-->>-R: Return file data
```
