using AppTIC.Web.Models.Domain;
using AppTIC.Web.Models.ViewModels;
using AppTIC.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AppTIC.Web.Controllers;

[Authorize(Roles = Roles.Admin)]
public class AdminController(UserManager<ApplicationUser> userManager, IActivityLog log, ICurrentUser currentUser) : Controller
{
    public async Task<IActionResult> Utilizadores()
    {
        var users = await userManager.Users.OrderBy(u => u.Email).ToListAsync();
        var admins = (await userManager.GetUsersInRoleAsync(Roles.Admin)).Select(u => u.Id).ToHashSet();
        var rows = users.Select(u => new UtilizadorRow(u.Id, u.Email ?? u.UserName ?? "", u.NomeCompleto, u.Entidade,
            admins.Contains(u.Id) ? Roles.Admin : Roles.Utilizador, u.Ativo,
            u.LockoutEnd is { } end && end > DateTimeOffset.UtcNow, u.CriadoEm.ToLocalTime())).ToList();
        return View(rows);
    }

    public IActionResult Criar() => View("Utilizador", new UtilizadorViewModel());

    public async Task<IActionResult> Editar(string id)
    {
        var user = await userManager.FindByIdAsync(id);
        if (user is null) return NotFound();
        return View("Utilizador", new UtilizadorViewModel
        {
            Id = user.Id,
            Email = user.Email ?? "",
            NomeCompleto = user.NomeCompleto,
            Entidade = user.Entidade,
            Ativo = user.Ativo,
            Perfil = await userManager.IsInRoleAsync(user, Roles.Admin) ? Roles.Admin : Roles.Utilizador
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Guardar(UtilizadorViewModel model)
    {
        if (!Roles.Todos.Contains(model.Perfil)) ModelState.AddModelError(nameof(model.Perfil), "Perfil inválido.");
        if (model.Id is null && string.IsNullOrEmpty(model.Password))
            ModelState.AddModelError(nameof(model.Password), "Indique a palavra-passe inicial.");
        if (model.Id == currentUser.UserId && (!model.Ativo || model.Perfil != Roles.Admin))
            ModelState.AddModelError(string.Empty, "Não pode desativar nem retirar o perfil Admin à sua própria conta.");
        if (!ModelState.IsValid) return View("Utilizador", model);

        var isNew = model.Id is null;
        var user = isNew ? new ApplicationUser { EmailConfirmed = true } : await userManager.FindByIdAsync(model.Id!);
        if (user is null) return NotFound();

        user.Email = user.UserName = model.Email.Trim();
        user.NomeCompleto = model.NomeCompleto;
        user.Entidade = model.Entidade;
        var wasActive = user.Ativo;
        user.Ativo = model.Ativo;

        var result = isNew ? await userManager.CreateAsync(user, model.Password!) : await userManager.UpdateAsync(user);
        if (result.Succeeded && !isNew && !string.IsNullOrEmpty(model.Password))
        {
            var token = await userManager.GeneratePasswordResetTokenAsync(user);
            result = await userManager.ResetPasswordAsync(user, token, model.Password);
        }
        if (!result.Succeeded)
        {
            foreach (var e in result.Errors) ModelState.AddModelError(string.Empty, e.Description);
            return View("Utilizador", model);
        }

        var roles = await userManager.GetRolesAsync(user);
        if (!roles.SequenceEqual([model.Perfil]))
        {
            await userManager.RemoveFromRolesAsync(user, roles);
            await userManager.AddToRoleAsync(user, model.Perfil);
        }
        // Termina as sessões abertas se a conta foi desativada ou mudou de perfil.
        if (wasActive && !user.Ativo || !roles.SequenceEqual([model.Perfil]))
            await userManager.UpdateSecurityStampAsync(user);

        await log.LogAsync(CategoriaRegisto.Administracao, isNew ? "Criar utilizador" : "Editar utilizador",
            $"Utilizador {user.Email} ({model.Perfil}{(user.Ativo ? "" : ", inativo")}).");
        TempData["Ok"] = "Utilizador guardado.";
        return RedirectToAction(nameof(Utilizadores));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Desbloquear(string id)
    {
        var user = await userManager.FindByIdAsync(id);
        if (user is null) return NotFound();
        await userManager.SetLockoutEndDateAsync(user, null);
        await userManager.ResetAccessFailedCountAsync(user);
        await log.LogAsync(CategoriaRegisto.Administracao, "Desbloquear", $"Utilizador {user.Email} desbloqueado.");
        TempData["Ok"] = "Utilizador desbloqueado.";
        return RedirectToAction(nameof(Utilizadores));
    }
}
