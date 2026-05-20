using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Threading.Tasks;
using BlazorApp.Api.Data;
using BlazorApp.Api.Services;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests
{
    public sealed class EmployeeProfileServiceTests : IDisposable
    {
        private readonly string _dbPath;
        private readonly SqliteConnection _sqliteConnection;
        private readonly SqlSugarClient _db;

        public EmployeeProfileServiceTests()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
            _sqliteConnection = new SqliteConnection($"Data Source={_dbPath}");
            _sqliteConnection.Open();

            _db = new SqlSugarClient(new ConnectionConfig
            {
                ConnectionString = _sqliteConnection.ConnectionString,
                DbType = DbType.Sqlite,
                IsAutoCloseConnection = false,
                InitKeyType = InitKeyType.Attribute,
            });

            _db.CodeFirst.InitTables<User, EmployeeProfile>();
        }

        [Fact]
        public async Task UpsertSelfAsync_WhenPayloadContainsAnotherUserGuid_OnlyUpdatesCurrentUser()
        {
            await SeedUsersAsync();

            await _db.Insertable(
                new EmployeeProfile
                {
                    EmployeeInfoId = 1,
                    UserGUID = "user-self",
                    Address = "Old self address",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                }
            ).ExecuteCommandAsync();

            await _db.Insertable(
                new EmployeeProfile
                {
                    EmployeeInfoId = 2,
                    UserGUID = "user-other",
                    Address = "Original other address",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                }
            ).ExecuteCommandAsync();

            var service = CreateService("user-self", "self_user");

            var result = await service.UpsertSelfAsync(
                new EmployeeProfileUpsertDto
                {
                    UserGUID = "user-other",
                    Address = "Updated by self endpoint",
                    Gender = "female",
                    EmploymentType = "partTime",
                }
            );

            Assert.True(result.Success);
            Assert.NotNull(result.Data);
            Assert.Equal("user-self", result.Data!.UserGUID);

            var selfProfile = await _db.Queryable<EmployeeProfile>()
                .FirstAsync(item => item.UserGUID == "user-self");
            var otherProfile = await _db.Queryable<EmployeeProfile>()
                .FirstAsync(item => item.UserGUID == "user-other");

            Assert.Equal("Updated by self endpoint", selfProfile.Address);
            Assert.Equal(EmployeeGender.Female, selfProfile.Gender);
            Assert.Equal(EmployeeType.PartTime, selfProfile.EmployeeType);
            Assert.Equal("Original other address", otherProfile.Address);
        }

        public void Dispose()
        {
            _db.Dispose();
            _sqliteConnection.Dispose();

            if (File.Exists(_dbPath))
            {
                File.Delete(_dbPath);
            }
        }

        private EmployeeProfileService CreateService(string userGuid, string username)
        {
            var httpContextAccessor = new HttpContextAccessor
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(
                        new ClaimsIdentity(CreateClaims(userGuid, username), "TestAuth")
                    ),
                },
            };

            var currentUserService = new CurrentUserService(httpContextAccessor);
            var context = CreateSqlSugarContext(_db);

            return new EmployeeProfileService(
                context,
                currentUserService,
                NullLogger<EmployeeProfileService>.Instance
            );
        }

        private async Task SeedUsersAsync()
        {
            await _db.Insertable(
                new[]
                {
                    new User
                    {
                        UserGUID = "user-self",
                        Username = "self_user",
                        Email = "self@example.com",
                        PasswordHash = "hashed",
                        FullName = "Self User",
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow,
                    },
                    new User
                    {
                        UserGUID = "user-other",
                        Username = "other_user",
                        Email = "other@example.com",
                        PasswordHash = "hashed",
                        FullName = "Other User",
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow,
                    },
                }
            ).ExecuteCommandAsync();
        }

        private static SqlSugarContext CreateSqlSugarContext(ISqlSugarClient db)
        {
            var context = (SqlSugarContext)RuntimeHelpers.GetUninitializedObject(
                typeof(SqlSugarContext)
            );

            var dbField = typeof(SqlSugarContext).GetField(
                "_db",
                BindingFlags.Instance | BindingFlags.NonPublic
            );
            dbField!.SetValue(context, db);

            return context;
        }

        private static IEnumerable<Claim> CreateClaims(string userGuid, string username)
        {
            yield return new Claim("userGuid", userGuid);
            yield return new Claim("userId", userGuid);
            yield return new Claim(ClaimTypes.NameIdentifier, userGuid);
            yield return new Claim(ClaimTypes.Name, username);
        }
    }
}
