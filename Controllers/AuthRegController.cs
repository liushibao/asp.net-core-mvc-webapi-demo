using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Redis.OM;
using SqlSugar;
using System.Security.Claims;
using WebApi.Models;
using WebApi.Services;

namespace WebApi.Controllers;

[ApiController]
[Authorize]
[Route("/api/auth/reg")]
public class AuthRegController : ControllerBase
{
    private readonly ISqlSugarClient _db;
    private readonly RedisConnectionProvider _redis;
    private readonly IMobVerifier _smsClient;

    public AuthRegController(ISqlSugarClient db, RedisConnectionProvider redis, IMobVerifier smsClient)
    {
        _smsClient = smsClient;
        _db = db;
        _redis = redis;
    }

    [HttpPost]
    [Route("SendSmsCode")]
    public async Task<IResult> SendSmsCode([FromBody] SendSmsCodeRequest req)
    {
        string userId = this.HttpContext?.User?.FindFirstValue("id");
        var count = await _db.Queryable<User>().CountAsync(t => t.Mob == req.Mob && t.Id != int.Parse(userId));
        if (count > 0)
            return Results.BadRequest(new ProblemDetails() { Title = "手机号已绑定其他用户。" });
        var smsCode = new Random().Next(1000000, 9999999).ToString().Substring(0, 6);
        var result = await this._smsClient.SendSmsCode(req.Mob, [smsCode]);
        if (result == true)
        {
            var redisCollection = this._redis.RedisCollection<SmsCodeCache>();
            await redisCollection.InsertAsync(new SmsCodeCache() { Mob = req.Mob, SmsCode = smsCode, UserId = userId }, new TimeSpan(0, 10, 0));
            return Results.Ok(new { isSuccess = true, expireSeconds = 600 });
        }
        else
        {
            return Results.Problem(new ProblemDetails() { Title = "短信服务异常" });
        }
    }

    [HttpPost]
    [Route("VerifySmsCode")]
    public async Task<IResult> VerifySmsCode(VerifySmsCodeRequest req)
    {
        string userId = this.HttpContext?.User?.FindFirstValue("id");
        var redisCollection = this._redis.RedisCollection<SmsCodeCache>();
        var smsCodeCache = await redisCollection.FindByIdAsync(userId);
        var isSuccess = smsCodeCache?.Mob == req.Mob && smsCodeCache?.SmsCode == req.SmsCode;
        if (isSuccess == true)
        {
            await _db.Updateable<User>().SetColumns(t => new User { Mob = smsCodeCache.Mob }).Where(t => t.Id == int.Parse(userId)).ExecuteCommandAsync();
        }
        return Results.Ok(new { isSuccess });
    }

    [HttpPost]
    [Route("Reg")]
    public async Task<IResult> Reg(UserRegRequest req)
    {
        string userId = this.HttpContext?.User?.FindFirstValue("id");
        await _db.Updateable<User>().SetColumns(t => new User()
        {
            Name = req.Name,
            IdCardNumber = req.IdCardNumber,
            Birthday = req.Birthday,
        }).Where(t => t.Id == int.Parse(userId) && t.Mob == req.Mob).ExecuteCommandAsync();
        var user = await _db.Queryable<User>().SingleAsync(t => t.Id == int.Parse(userId));
        return Results.Ok(new { isSuccess = true, user });
    }

}
