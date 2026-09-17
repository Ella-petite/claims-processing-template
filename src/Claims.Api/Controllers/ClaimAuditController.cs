using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Claims.Api.Data;
using Claims.Contracts.Models;

namespace Claims.Api.Controllers
{
    [ApiController]
    [Route("api/claims")]
    public class ClaimAuditController : ControllerBase
    {
        private readonly ClaimsDbContext _dbContext;

        public ClaimAuditController(ClaimsDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        [HttpGet("audit")]
        public async Task<ActionResult<IEnumerable<ClaimAuditDto>>> GetAudit([FromQuery] ClaimAuditFilter filter)
        {
            var query = _dbContext.Claims
                .Include(c => c.History)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(filter?.Status))
            {
                query = query.Where(c => c.Status.ToString() == filter.Status);
            }

            if (filter?.StartDate != null)
            {
                query = query.Where(c => c.Created >= filter.StartDate);
            }

            if (filter?.EndDate != null)
            {
                query = query.Where(c => c.Created <= filter.EndDate);
            }

            var result = await query.Select(c => new ClaimAuditDto
            {
                ClaimId = c.Id,
                ClaimReference = c.ClaimReference,
                Status = c.Status.ToString(),
                Amount = c.Amount,
                Currency = "USD", // Assuming currency stored elsewhere; placeholder
                WorkflowInstanceId = c.WorkflowInstanceId,
                Created = c.IncidentDate,
                Updated = c.IncidentDate, // Placeholder; could be a modified timestamp if present
                History = c.History.Select(h => new ClaimHistoryDto
                {
                    At = h.At,
                    Status = h.Status.ToString(),
                    Source = h.Source,
                    Note = h.Note,
                    CorrelationId = h.CorrelationId
                }).ToList()
            }).ToListAsync();

            return Ok(result);
        }
    }
}
