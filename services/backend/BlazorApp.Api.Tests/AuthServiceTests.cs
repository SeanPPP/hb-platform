using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using BlazorApp.Api.Data;
using BlazorApp.Api.Services;
using BlazorApp.Api.Utils;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests
{
    public sealed class AuthServiceTests : IDisposable
    {
        private readonly string _dbPath;
        private readonly SqliteConnection _sqliteConnection;
        private readonly SqlSugarClient _db;

        public AuthServiceTests()
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
        }

        [Fact]
        public async Task LoginAsync_WhenUserTableHasNoPhoneColumn_StillAuthenticates()
        {
            CreateLegacyUserSchemaWithoutPhone();
            await SeedLegacyUserAsync();

            var service = new AuthService(
                CreateSqlSugarContext(_db),
                CreateJwtConfiguration(),
                new HttpContextAccessor()
            );

            var result = await service.LoginAsync(new LoginRequest
            {
                Username = "admin",
                Password = "Secret123",
            });

            Assert.True(result.Success);
            Assert.Equal("admin", result.User?.Username);
        }

        [Fact]
        public async Task RefreshTokensAsync_WhenUserIsInactive_ReturnsNullAndRevokesRefreshToken()
        {
            await CreateCurrentAuthSchemaAsync();

            const string userGuid = "inactive-user";
            const string refreshToken = "refresh-token-inactive";

            await _db.Insertable(
                new User
                {
                    UserGUID = userGuid,
                    Username = "inactive-user",
                    Email = "inactive@example.com",
                    PasswordHash = PasswordHasher.HashPassword("Secret123"),
                    FullName = "Inactive User",
                    IsActive = false,
                    IsDeleted = false,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                }
            ).ExecuteCommandAsync();

            await _db.Insertable(
                new RefreshToken
                {
                    RefreshTokenGUID = Guid.NewGuid().ToString(),
                    UserGUID = userGuid,
                    Token = refreshToken,
                    ExpiresAt = DateTime.UtcNow.AddDays(1),
                    IsRevoked = false,
                    IsDeleted = false,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                }
            ).ExecuteCommandAsync();

            var service = new AuthService(
                CreateSqlSugarContext(_db),
                CreateJwtConfiguration(),
                new HttpContextAccessor()
            );

            var result = await service.RefreshTokensAsync(
                accessToken: string.Empty,
                refreshToken: refreshToken,
                ipAddress: "127.0.0.1",
                userAgent: "xunit"
            );

            Assert.Null(result);

            var storedToken = await _db.Queryable<RefreshToken>()
                .FirstAsync(token => token.Token == refreshToken);
            Assert.NotNull(storedToken);
            Assert.True(storedToken!.IsRevoked);
        }

        [Fact]
        public void GenerateJwtToken_WithLegacyLocalInvociePermission_AddsCanonicalLocalPurchaseClaim()
        {
            var service = new AuthService(
                CreateSqlSugarContext(_db),
                CreateJwtConfiguration(),
                new HttpContextAccessor()
            );
            var user = new User
            {
                UserGUID = "user-legacy",
                Username = "legacy-user",
                Email = "legacy@example.test",
                Roles = new List<Role> { new() { RoleName = "User" } },
            };

            var token = service.GenerateJwtToken(user, new List<string> { "LocalInvocie.View" });
            var claims = new JwtSecurityTokenHandler().ReadJwtToken(token).Claims.ToList();

            Assert.Contains(claims, claim => claim.Type == "permission" && claim.Value == "LocalInvocie.View");
            Assert.Contains(claims, claim =>
                claim.Type == "permission" && claim.Value == Permissions.LocalPurchase.View
            );
        }

        public void Dispose()
        {
            _db.Dispose();
            _sqliteConnection.Dispose();

            if (File.Exists(_dbPath))
            {
                SqliteTempFileCleanup.DeleteIfExists(_dbPath);
            }
        }

        private void CreateLegacyUserSchemaWithoutPhone()
        {
            _db.Ado.ExecuteCommand(
                """
                CREATE TABLE [User] (
                    UserGUID TEXT NOT NULL PRIMARY KEY,
                    Username TEXT NOT NULL,
                    Email TEXT NOT NULL,
                    PasswordHash TEXT NOT NULL,
                    FullName TEXT NULL,
                    LastLoginAt TEXT NULL,
                    IsActive INTEGER NOT NULL,
                    CreatedAt TEXT NOT NULL,
                    CreatedBy TEXT NULL,
                    UpdatedAt TEXT NULL,
                    UpdatedBy TEXT NULL,
                    IsDeleted INTEGER NOT NULL
                );

                CREATE TABLE [Role] (
                    RoleGUID TEXT NOT NULL PRIMARY KEY,
                    RoleName TEXT NOT NULL,
                    Description TEXT NULL,
                    IsActive INTEGER NOT NULL,
                    CreatedAt TEXT NOT NULL,
                    CreatedBy TEXT NULL,
                    UpdatedAt TEXT NULL,
                    UpdatedBy TEXT NULL,
                    IsDeleted INTEGER NOT NULL
                );

                CREATE TABLE [UserRole] (
                    UserRoleGUID TEXT NOT NULL PRIMARY KEY,
                    UserGUID TEXT NOT NULL,
                    RoleGUID TEXT NOT NULL,
                    AssignedAt TEXT NOT NULL,
                    AssignedByGUID TEXT NULL,
                    CreatedAt TEXT NOT NULL,
                    CreatedBy TEXT NULL,
                    UpdatedAt TEXT NULL,
                    UpdatedBy TEXT NULL,
                    IsDeleted INTEGER NOT NULL
                );
                """
            );
        }

        private async Task SeedLegacyUserAsync()
        {
            await _db.Ado.ExecuteCommandAsync(
                """
                INSERT INTO [User] (
                    UserGUID, Username, Email, PasswordHash, FullName, LastLoginAt, IsActive,
                    CreatedAt, CreatedBy, UpdatedAt, UpdatedBy, IsDeleted
                )
                VALUES (
                    @UserGUID, @Username, @Email, @PasswordHash, @FullName, NULL, 1,
                    @CreatedAt, NULL, @UpdatedAt, NULL, 0
                );
                """,
                new List<SugarParameter>
                {
                    new("@UserGUID", Guid.NewGuid().ToString()),
                    new("@Username", "admin"),
                    new("@Email", "admin@example.com"),
                    new("@PasswordHash", PasswordHasher.HashPassword("Secret123")),
                    new("@FullName", "Admin User"),
                    new("@CreatedAt", DateTime.UtcNow),
                    new("@UpdatedAt", DateTime.UtcNow),
                }
            );
        }

        private Task CreateCurrentAuthSchemaAsync()
        {
            _db.CodeFirst.InitTables<User, Role, UserRole, RefreshToken, SysRolePermission>();
            return Task.CompletedTask;
        }

        private static IConfiguration CreateJwtConfiguration()
        {
            return new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Jwt:Key"] = "abcdefghijklmnopqrstuvwxyz123456",
                    ["Jwt:Issuer"] = "BlazorApp.Tests",
                    ["Jwt:Audience"] = "BlazorApp.Tests",
                    ["Jwt:ExpireMinutes"] = "480",
                })
                .Build();
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
    }
}
