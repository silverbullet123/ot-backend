using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using OT.Assessment.Application.Interfaces;
using OT.Assessment.Core.Dtos;
using System.Threading.Tasks;

namespace OT.Assessment.Application.Services
{
    public class FailedWagerService
    {
        private readonly IFailedWagerRepository _repo;
        private readonly ILogger<FailedWagerService> _logger;

        public FailedWagerService(IFailedWagerRepository repo, ILogger<FailedWagerService> logger)
        {
            _repo = repo;
            _logger = logger;
        }

        public async Task SaveFailedWagerAsync(CasinoWagerDto dto)
        {
            try
            {
                var rawMessage = JsonSerializer.Serialize(dto);
                await _repo.InsertFailedWagerAsync(dto, rawMessage);
                _logger.LogInformation("Saved DLQ message to database. WagerId={WagerId}", dto.WagerId);
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "Failed to persist DLQ message. WagerId={WagerId}", dto.WagerId);
            }
        }
    }
}
