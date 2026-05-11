namespace DorisStorageAdapter.Services.Contract.Audit;

public sealed record AuditUser
{
    public string? EduPersonPrincipalName { get; init; }
    public string? Email { get; init; }
    public string? FamilyName { get; init; }
    public string? GivenName { get; init; }
    public string? Name { get; init; }
    public string? Orcid { get; init; }
}
