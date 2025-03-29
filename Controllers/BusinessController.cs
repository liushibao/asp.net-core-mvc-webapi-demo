using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.IdentityModel.Tokens;
using Redis.OM;
using Redis.OM.Searching;
using SqlSugar;
using System.Drawing.Printing;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Web;
using WebApi.Models;
using WebApi.Services;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace WebApi.Controllers;

[ApiController]
[Route("/api/[controller]/public")]
public class BusinessController : ControllerBase
{
    private readonly ISqlSugarClient _db;
    private readonly RedisConnectionProvider _redis;

    public BusinessController(ISqlSugarClient db, RedisConnectionProvider redis)
    {
        _db = db;
        _redis = redis;
    }

    [HttpGet]
    [Route("GdpData")]
    public async Task<IResult> GdpData([FromQuery] GdpDataRequest req)
    {
        int yearStart = (int)req.YearStart;
        int yearEnd = (int)req.YearEnd;
        string query = $"{yearStart}-{yearEnd}";
        var redisCollection = this._redis.RedisCollection<GdpRes>();
        GdpRes cached = await redisCollection!.FindByIdAsync(query);
        if (cached == null)
        {
            Console.WriteLine("gdp cach not found");
            var data = await _db.Queryable<Gdp>().Where(t => t.Year >= yearStart && t.Year <= yearEnd).ToListAsync();
            cached = new GdpRes() { Query = query, Data = data.ToArray() };
            await redisCollection.InsertAsync(cached, new TimeSpan(24, 0, 0));
        }
        return Results.Ok(cached.Data);
    }

    [HttpGet]
    [Route("Info")]
    public async Task<IResult> Info([FromQuery] InfoRequest req)
    {
        int pageNumber = (int)req.PageNumber;
        int pageSize = (int)req.PageSize;
        RefAsync<int> totalCount = 0;
        RefAsync<int> totalPage = 0;
        string query = $"{pageNumber}-{pageSize}";
        var redisCollection = this._redis.RedisCollection<InfoRes>();
        InfoRes cached = await redisCollection!.FindByIdAsync(query);
        if (cached == null)
        {
            Console.WriteLine("info cach not found");
            var data = await _db.Queryable<Info>().ToPageListAsync(pageNumber, pageSize, totalCount, totalPage);
            cached = new InfoRes()
            {
                Query = query,
                Data = new PagedData<Info>
                {
                    PageNumber = pageNumber,
                    PageSize = pageSize,
                    TotalCount = totalCount.Value,
                    TotalPage = totalPage.Value,
                    Data = data.ToArray()
                }
            };
            await redisCollection.InsertAsync(cached, new TimeSpan(0, 10, 0));
        }
        return Results.Ok(cached.Data);
    }

}
