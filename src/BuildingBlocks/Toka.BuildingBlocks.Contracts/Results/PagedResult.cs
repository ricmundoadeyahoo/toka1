namespace Toka.BuildingBlocks.Contracts.Results;

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, long TotalCount)
{
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

/// <summary>Normaliza parámetros de paginación (defensa contra page/pageSize abusivos).</summary>
public static class Paging
{
    public const int MaxPageSize = 100;

    public static (int Page, int PageSize) Normalize(int? page, int? pageSize, int defaultSize = 20)
    {
        var p = page is null or < 1 ? 1 : page.Value;
        var s = pageSize is null or < 1 ? defaultSize : Math.Min(pageSize.Value, MaxPageSize);
        return (p, s);
    }
}
