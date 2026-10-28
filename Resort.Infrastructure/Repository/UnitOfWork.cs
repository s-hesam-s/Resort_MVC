using Microsoft.EntityFrameworkCore;
using System.Data;
using Resort.Application.Common.Interfaces;
using Resort.Domain.Entities;
using Resort.Infrastructure.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Resort.Infrastructure.Repository
{
    public class UnitOfWork : IUnitOfWork
    {
        private readonly ApplicationDbContext _db;
        public IVillaRepository Villa { get; private set; }
        public IVillaNumberRepository VillaNumber { get; private set; }
        public IAmenityRepository Amenity { get; private set; }
        public IBookingRepository Booking { get; private set; }
        public IApplicationUserRepository User { get; private set; }
        public UnitOfWork(ApplicationDbContext db)
        {
            _db = db;
            Villa = new VillaRepository(_db);
            VillaNumber = new VillaNumberRepository(_db);
            Amenity = new AmenityRepository(_db);
            Booking = new BookingRepository(_db);
            User = new ApplicationUserRepository(_db);
        }
        public T ExecuteWithVillaLock<T>(int villaId, Func<T> operation)
        {
            using var transaction = _db.Database.BeginTransaction(IsolationLevel.Serializable);
            var villa = _db.Villas.FromSqlInterpolated(
                $"SELECT * FROM [Villas] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {villaId}")
                .AsNoTracking().SingleOrDefault();
            if (villa == null) throw new InvalidOperationException("The villa no longer exists.");
            var result = operation();
            transaction.Commit();
            return result;
        }

        public void Save()
        {
            _db.SaveChanges();
        }
    }
}
