﻿using Resort.Application.Common.Interfaces;
using Resort.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace Resort.Infrastructure.Repository
{
    public class VillaRepository : IVillaRepository
    {
        public void Add(Villa entity)
        {
            throw new NotImplementedException();
        }

        public IEnumerable<Villa> Get(Expression<Func<Villa, bool>> filter, string? includeProperties = null)
        {
            throw new NotImplementedException();
        }

        public IEnumerable<Villa> GetAll(Expression<Func<Villa, bool>>? filter = null, string? includeProperties = null)
        {
            throw new NotImplementedException();
        }

        public void Remove(Villa entity)
        {
            throw new NotImplementedException();
        }

        public void Save()
        {
            throw new NotImplementedException();
        }

        public void Update(Villa entity)
        {
            throw new NotImplementedException();
        }
    }
}
