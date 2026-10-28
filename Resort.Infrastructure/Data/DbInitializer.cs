using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Resort.Application.Common.Interfaces;
using Resort.Application.Common.Utility;
using Resort.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Resort.Infrastructure.Data
{
    public class DbInitializer: IDbInitializer
    {
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly ApplicationDbContext _db;

        public DbInitializer(
            RoleManager<IdentityRole> roleManager,
            ApplicationDbContext db)
        {
            _roleManager = roleManager;
            _db = db;
        }

        public void Initialize()
        {
            try
            {
                if (_db.Database.GetPendingMigrations().Count() > 0)
                {
                    _db.Database.Migrate();
                }

                foreach (var role in new[] { SD.Role_Admin, SD.Role_Customer })
                {
                    if (!_roleManager.RoleExistsAsync(role).GetAwaiter().GetResult())
                    {
                        var result = _roleManager.CreateAsync(new IdentityRole(role)).GetAwaiter().GetResult();
                        if (!result.Succeeded)
                        {
                            throw new InvalidOperationException("Unable to initialize application roles.");
                        }
                    }
                }
            }
            catch (Exception)
            {
                throw;
            }
        }
    }
}
