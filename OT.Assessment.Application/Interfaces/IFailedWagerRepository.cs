using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OT.Assessment.Core.Dtos;
using System.Threading;
using System.Threading.Tasks;

namespace OT.Assessment.Application.Interfaces
{
    public interface IFailedWagerRepository
    {
        Task InsertFailedWagerAsync(CasinoWagerDto dto, string rawMessage, CancellationToken ct = default);
    }
}
