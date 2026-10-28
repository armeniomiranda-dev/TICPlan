using AppTIC.Web.Data;
using AppTIC.Web.Models.ViewModels;
using AppTIC.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AppTIC.Web.Controllers;

/// <summary>Registo de atividade (só Admin; vê todos os logins).</summary>
[Authorize(Roles = Roles.Admin)]
public class RegistosController(ApplicationDbContext db, ICurrentUser currentUser) : Controller
{
    public async Task<IActionResult> Index(string? categoria = null, int dias = 30)
    {
        var desde = DateTime.UtcNow.AddDays(-Math.Clamp(dias, 1, 3650));
        var query = db.Registos.Where(r => r.Data >= desde);
        if (!string.IsNullOrEmpty(categoria)) query = query.Where(r => r.Categoria == categoria);

        var isAdmin = currentUser.IsAdmin;
        var data = await query.OrderByDescending(r => r.Id).Take(5000)
            .Select(r => new { r, Dono = isAdmin ? r.Owner!.Email : null }).ToListAsync();
        ViewBag.Categoria = categoria;
        ViewBag.Dias = dias;
        return View(data.Select(x => new RegistoRow(x.r.Id, x.r.Data.ToLocalTime(), x.r.Nivel.ToString(), x.r.Categoria,
            x.r.Acao, x.r.Mensagem, x.r.GtfsFeedId, x.r.UserName, x.Dono)).ToList());
    }
}
