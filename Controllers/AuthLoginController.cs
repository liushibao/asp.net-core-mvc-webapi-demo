using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.IdentityModel.Tokens;
using Redis.OM;
using SqlSugar;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Web;
using WebApi.Models;
using WebApi.Services;

namespace WebApi.Controllers;

[ApiController]
[Route("/api/auth/login")]
public class AuthLoginController : ControllerBase
{
    private readonly ISqlSugarClient _db;
    private readonly RedisConnectionProvider _redis;

    public AuthLoginController(ISqlSugarClient db, RedisConnectionProvider redis)
    {
        _db = db;
        _redis = redis;
    }

    private string GenerateToken(string userId)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var tokenDescriptor = new SecurityTokenDescriptor()
        {
            Subject = new ClaimsIdentity(new Claim[]
            {
                new Claim("id", userId),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new Claim(JwtRegisteredClaimNames.Iss, EnvironmentConfig.Instance.Issuer),
                new Claim(JwtRegisteredClaimNames.Aud, EnvironmentConfig.Instance.Audience)
            }),
            Expires = DateTime.Now.AddHours(2),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.ASCII.GetBytes(EnvironmentConfig.Instance.JwtKey)), SecurityAlgorithms.HmacSha256)
        };
        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }

    /// <summary>
    /// 由前端App根据身份认证需要调用
    /// </summary>
    /// <param name="redirect_uri">用户打开前端App需要身份认证的页面时的相对路径</param>
    /// <param name="context"></param>
    /// <returns></returns>
    public IResult Get([FromQuery] LoginRequest req)
    {
        string redirect_uri = req.redirect_uri;
        // 根据微信开发文档 https://developers.weixin.qq.com/doc/offiaccount/OA_Web_Apps/Wechat_webpage_authorization.html#0
        var url = EnvironmentConfig.Instance.WxAppId == null
           ? $"{(Request.IsHttps ? "https" : "http")}://{Request.Host}/api/auth/login/FakeWeixinLogin?redirect_uri={HttpUtility.UrlEncode(redirect_uri)}&response_type=code&scope=snsapi_base#wechat_redirect"
           : $"https://open.weixin.qq.com/connect/oauth2/authorize?appid={EnvironmentConfig.Instance.WxAppId}&redirect_uri={HttpUtility.UrlEncode(redirect_uri)}&response_type=code&scope=snsapi_base#wechat_redirect";
        Console.WriteLine(url);
        return Results.Redirect(url);
    }


    [HttpGet]
    [Route("FakeWeixinLogin")]
    public IResult FakeWeixinLogin([FromQuery] FakeWeixinLoginRequest req)
    {
        string redirect_uri = req.redirect_uri;
        return Results.Redirect($"{redirect_uri}?code=1113");
    }

    [Route("Token")]
    public async Task<IResult> GetToken([FromQuery] GetTokenRequest req)
    {
        string code = req.code;
        string wxOpenId;
        if (EnvironmentConfig.Instance.WxAppId != null)
        {
            // 根据微信开发文档 https://developers.weixin.qq.com/doc/offiaccount/OA_Web_Apps/Wechat_webpage_authorization.html#1
            HttpClient client = new HttpClient();
            HttpResponseMessage response = await client.GetAsync($"https://api.weixin.qq.com/sns/oauth2/access_token?appid=${EnvironmentConfig.Instance.WxAppId}&secret=${EnvironmentConfig.Instance.WxAppSecret}&code=${code}&grant_type=authorization_code");
            response.EnsureSuccessStatusCode();
            var wxToken = await response.Content.ReadFromJsonAsync<dynamic>();
            if (wxToken == null)
                return Results.Problem(new ProblemDetails() { Title = "微信ID返回空值" });
            else if ((string)wxToken.errcode != null)
                return Results.Problem(new ProblemDetails() { Title = "微信ID返回错误" });
            else
            {
                wxOpenId = wxToken.openid;
                var redisCollection = this._redis.RedisCollection<WxToken>();
                await redisCollection.InsertAsync(wxToken as WxToken, new TimeSpan(0, Math.Floor(wxToken.expiresIn / 3600), 0));
            }
        }
        else
        {
            // 开发环境执行
            wxOpenId = code;
        }
        var user = await this._db.Queryable<User>().SingleAsync(t => t.WxOpenId == wxOpenId);
        if (user == null)
        {
            user = new User() { WxOpenId = wxOpenId };
            await this._db.Insertable<User>(user).ExecuteCommandAsync();
        }

        var token = GenerateToken(user.Id.ToString());

        return Results.Ok(new
        {
            token,
            user
        });
    }
}
