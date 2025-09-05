using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace OT.Assessment.Infrastructure.Data
{
    public class DapperConnectionFactory
    {
        private readonly IConfiguration _config;
        public DapperConnectionFactory(IConfiguration config) => _config = config;

        public SqlConnection CreateConnection()
        {
            var cs = _config.GetConnectionString("Default");
            var conn = new SqlConnection(cs);
            return conn;
        }
    }
}
