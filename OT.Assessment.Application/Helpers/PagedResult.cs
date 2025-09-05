using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OT.Assessment.Application.Helpers
{
    public sealed record PagedResult<T>(IReadOnlyList<T> Data, int Page, int PageSize, int Total)
    {
        public int TotalPages => (int)Math.Ceiling(Total / (double)PageSize);
    }
}
