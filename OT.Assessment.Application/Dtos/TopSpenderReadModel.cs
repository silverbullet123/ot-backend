using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OT.Assessment.Application.Dtos
{
    public record TopSpenderReadModel(Guid AccountId, string Username, decimal TotalAmountSpend);
}
