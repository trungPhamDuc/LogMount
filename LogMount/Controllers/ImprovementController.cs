using LogMount.Data;
using LogMount.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;

namespace LogMount.Controllers;

[ApiController]
[Route("api/improve")]
[Authorize]
public class ImprovementController : ControllerBase
{
    private readonly LogMountDbContext _dbContext;

    public ImprovementController(LogMountDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpPost("retry")]
    [Authorize(Roles = UserRoles.StaffOrAdmin)]
    public async Task<IActionResult> CreateRetryImprove([FromBody] RetryImprove model)
    {
        if (model == null) return BadRequest(new { success = false, message = "Dữ liệu không hợp lệ." });
        if (string.IsNullOrWhiteSpace(model.EngineerName) || string.IsNullOrWhiteSpace(model.ActionTaken))
        {
            return BadRequest(new { success = false, message = "Vui lòng nhập Tên kỹ sư và Hành động cải thiện." });
        }

        await _dbContext.Database.EnsureCreatedAsync();

        model.CreatedAt = DateTime.Now;
        _dbContext.RetryImproves.Add(model);
        await _dbContext.SaveChangesAsync();

        return Ok(new { success = true, message = "Đã lưu nhật ký cải thiện RetryLog thành công!" });
    }

    [HttpPut("retry/{id}")]
    [Authorize(Roles = UserRoles.StaffOrAdmin)]
    public async Task<IActionResult> UpdateRetryImprove(int id, [FromBody] RetryImprove model)
    {
        var item = await _dbContext.RetryImproves.FindAsync(id);
        if (item == null) return NotFound(new { success = false, message = "Không tìm thấy bản ghi." });

        if (string.IsNullOrWhiteSpace(model.EngineerName) || string.IsNullOrWhiteSpace(model.ActionTaken))
        {
            return BadRequest(new { success = false, message = "Vui lòng nhập Tên kỹ sư và Hành động cải thiện." });
        }

        item.PartsName = model.PartsName;
        item.Line = model.Line;
        item.Lane = model.Lane;
        item.Side = model.Side;
        item.Machine = model.Machine;
        item.Feeder = model.Feeder;
        item.EngineerName = model.EngineerName;
        item.ActionTaken = model.ActionTaken;
        item.ExecutionDate = model.ExecutionDate;

        await _dbContext.SaveChangesAsync();
        return Ok(new { success = true, message = "Đã cập nhật nhật ký cải thiện RetryLog thành công!" });
    }

    [HttpDelete("retry/{id}")]
    [Authorize(Roles = UserRoles.StaffOrAdmin)]
    public async Task<IActionResult> DeleteRetryImprove(int id)
    {
        var item = await _dbContext.RetryImproves.FindAsync(id);
        if (item == null) return NotFound(new { success = false, message = "Không tìm thấy bản ghi." });

        _dbContext.RetryImproves.Remove(item);
        await _dbContext.SaveChangesAsync();
        return Ok(new { success = true, message = "Đã xóa nhật ký cải thiện RetryLog thành công!" });
    }

    [HttpGet("retry/list")]
    public async Task<IActionResult> GetRetryImproveList([FromQuery] string? fromDate, [FromQuery] string? toDate, [FromQuery] string? partsName)
    {
        await _dbContext.Database.EnsureCreatedAsync();
        var query = _dbContext.RetryImproves.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(fromDate) && DateTime.TryParse(fromDate, out var fromDt))
        {
            query = query.Where(x => x.ExecutionDate >= fromDt.Date);
        }

        if (!string.IsNullOrWhiteSpace(toDate) && DateTime.TryParse(toDate, out var toDt))
        {
            query = query.Where(x => x.ExecutionDate <= toDt.Date);
        }

        if (!string.IsNullOrWhiteSpace(partsName))
        {
            var p = partsName.Trim();
            query = query.Where(x => x.PartsName != null && x.PartsName.Contains(p));
        }

        var list = await query
            .OrderByDescending(x => x.ExecutionDate)
            .ThenByDescending(x => x.Id)
            .Take(200)
            .ToListAsync();
        return Ok(list);
    }

    [HttpPost("error")]
    [Authorize(Roles = UserRoles.StaffOrAdmin)]
    public async Task<IActionResult> CreateErrorImprove([FromBody] ErrorImprove model)
    {
        if (model == null) return BadRequest(new { success = false, message = "Dữ liệu không hợp lệ." });
        if (string.IsNullOrWhiteSpace(model.EngineerName) || string.IsNullOrWhiteSpace(model.ActionTaken))
        {
            return BadRequest(new { success = false, message = "Vui lòng nhập Tên kỹ sư và Hành động cải thiện." });
        }

        await _dbContext.Database.EnsureCreatedAsync();

        model.CreatedAt = DateTime.Now;
        _dbContext.ErrorImproves.Add(model);
        await _dbContext.SaveChangesAsync();

        return Ok(new { success = true, message = "Đã lưu nhật ký cải thiện ErrorLog thành công!" });
    }

    [HttpPut("error/{id}")]
    [Authorize(Roles = UserRoles.StaffOrAdmin)]
    public async Task<IActionResult> UpdateErrorImprove(int id, [FromBody] ErrorImprove model)
    {
        var item = await _dbContext.ErrorImproves.FindAsync(id);
        if (item == null) return NotFound(new { success = false, message = "Không tìm thấy bản ghi." });

        if (string.IsNullOrWhiteSpace(model.EngineerName) || string.IsNullOrWhiteSpace(model.ActionTaken))
        {
            return BadRequest(new { success = false, message = "Vui lòng nhập Tên kỹ sư và Hành động cải thiện." });
        }

        item.Error = model.Error;
        item.Line = model.Line;
        item.Lane = model.Lane;
        item.Side = model.Side;
        item.Machine = model.Machine;
        item.EngineerName = model.EngineerName;
        item.ActionTaken = model.ActionTaken;
        item.ExecutionDate = model.ExecutionDate;

        await _dbContext.SaveChangesAsync();
        return Ok(new { success = true, message = "Đã cập nhật nhật ký cải thiện ErrorLog thành công!" });
    }

    [HttpDelete("error/{id}")]
    [Authorize(Roles = UserRoles.StaffOrAdmin)]
    public async Task<IActionResult> DeleteErrorImprove(int id)
    {
        var item = await _dbContext.ErrorImproves.FindAsync(id);
        if (item == null) return NotFound(new { success = false, message = "Không tìm thấy bản ghi." });

        _dbContext.ErrorImproves.Remove(item);
        await _dbContext.SaveChangesAsync();
        return Ok(new { success = true, message = "Đã xóa nhật ký cải thiện ErrorLog thành công!" });
    }

    [HttpGet("error/list")]
    public async Task<IActionResult> GetErrorImproveList([FromQuery] string? fromDate, [FromQuery] string? toDate, [FromQuery] string? error)
    {
        await _dbContext.Database.EnsureCreatedAsync();
        var query = _dbContext.ErrorImproves.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(fromDate) && DateTime.TryParse(fromDate, out var fromDt))
        {
            query = query.Where(x => x.ExecutionDate >= fromDt.Date);
        }

        if (!string.IsNullOrWhiteSpace(toDate) && DateTime.TryParse(toDate, out var toDt))
        {
            query = query.Where(x => x.ExecutionDate <= toDt.Date);
        }

        if (!string.IsNullOrWhiteSpace(error))
        {
            var err = error.Trim();
            query = query.Where(x => x.Error != null && x.Error.Contains(err));
        }

        var list = await query
            .OrderByDescending(x => x.ExecutionDate)
            .ThenByDescending(x => x.Id)
            .Take(200)
            .ToListAsync();
        return Ok(list);
    }
}
