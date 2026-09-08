namespace Counterpoint.Application.Catalogue;

/// <summary>What <see cref="IBrandMaintenance"/> needs to create or rename a brand (FR-2.21).</summary>
public sealed record SaveBrandCommand(string Name);
