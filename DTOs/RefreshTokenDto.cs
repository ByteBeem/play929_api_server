
using System;
using Play929Backend.DTOs;
using Play929Backend.Models;

    namespace Play929Backend.DTOs
{

public class RefreshTokenDto
{
    public string Token { get; set; }
    public DateTime Expires { get; set; }
}
}