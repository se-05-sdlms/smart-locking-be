namespace smart_locking_be.Application.DTOs.Common;

public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int TotalCount,
    int PageNumber,
    int PageSize
) : IReadOnlyCollection<T>
{
    [System.Text.Json.Serialization.JsonIgnore]
    public int Count => Items.Count;

    public IEnumerator<T> GetEnumerator() => Items.GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}
