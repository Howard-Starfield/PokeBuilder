using Microsoft.IdentityModel.Tokens;
using System;
using System.IdentityModel.Tokens.Jwt;
using System.Text;

namespace SysBot.Pokemon.WinForms.WebApi;

public static class JwtValidator
{
    /// <summary>
    /// Validates a Supabase JWT using the project's JWT secret.
    /// Returns true if the token is valid and not expired.
    /// </summary>
    public static bool Validate(string token, string jwtSecret)
    {
        if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(jwtSecret))
            return false;

        try
        {
            var handler = new JwtSecurityTokenHandler();
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret));

            handler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuer = false,
                ValidateAudience = false,
                ValidateLifetime = true,
                IssuerSigningKey = key,
                ClockSkew = TimeSpan.FromMinutes(1),
            }, out _);

            return true;
        }
        catch
        {
            return false;
        }
    }
}
