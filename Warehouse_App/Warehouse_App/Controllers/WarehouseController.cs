using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using System;
using System.Data;
using System.Data.SqlClient;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Warehouse_App.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class WarehouseController : ControllerBase
    {
        private readonly string _connectionString;

        public WarehouseController(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection");
        }

        [HttpPost("add-product")]
        public async Task<IActionResult> AddProductToWarehouse([FromBody] JObject data)
        {
            int productId = data["ProductId"].ToObject<int>();
            int warehouseId = data["WarehouseId"].ToObject<int>();
            int amount = data["Amount"].ToObject<int>();
            DateTime createdAt = data["CreatedAt"].ToObject<DateTime>();

            if (amount <= 0)
            {
                return BadRequest("Amount must be greater than 0");
            }

            using (SqlConnection connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                using (SqlTransaction transaction = connection.BeginTransaction())
                {
                    try
                    {
                        // Check if product exists
                        if (!await ProductExists(productId, connection, transaction))
                        {
                            return NotFound("Product not found");
                        }

                        // Check if warehouse exists
                        if (!await WarehouseExists(warehouseId, connection, transaction))
                        {
                            return NotFound("Warehouse not found");
                        }

                        // Check if order exists and is valid
                        if (!await OrderExistsAndValid(productId, amount, createdAt, connection, transaction))
                        {
                            return BadRequest("Invalid order");
                        }

                        // Update order
                        await UpdateOrder(productId, amount, connection, transaction);

                        // Insert record into Product_Warehouse
                        int newRecordId = await InsertProductWarehouseRecord(productId, warehouseId, amount, createdAt, connection, transaction);

                        transaction.Commit();
                        return Ok(newRecordId);
                    }
                    catch (Exception ex)
                    {
                        transaction.Rollback();
                        // Here you could add logging of the exception, e.g. using ILogger
                        return StatusCode(500, ex.Message);
                    }
                }
            }
        }

        [HttpPost("add-product-via-procedure")]
        public async Task<IActionResult> AddProductToWarehouseViaProcedure([FromBody] JObject data)
        {
            int productId = data["ProductId"].ToObject<int>();
            int warehouseId = data["WarehouseId"].ToObject<int>();
            int amount = data["Amount"].ToObject<int>();
            DateTime createdAt = data["CreatedAt"].ToObject<DateTime>();

            if (amount <= 0)
            {
                return BadRequest("Amount must be greater than 0");
            }

            using (SqlConnection connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                using (SqlCommand command = new SqlCommand("AddProductToWarehouse", connection))
                {
                    command.CommandType = CommandType.StoredProcedure;

                    command.Parameters.AddWithValue("@IdProduct", productId);
                    command.Parameters.AddWithValue("@IdWarehouse", warehouseId);
                    command.Parameters.AddWithValue("@Amount", amount);
                    command.Parameters.AddWithValue("@CreatedAt", createdAt);

                    try
                    {
                        var result = await command.ExecuteScalarAsync();
                        return Ok(new { NewId = result });
                    }
                    catch (SqlException ex)
                    {
                        // Here you could add logging of the exception, e.g. using ILogger
                        return StatusCode(500, ex.Message);
                    }
                }
            }
        }

        private async Task<bool> ProductExists(int productId, SqlConnection connection, SqlTransaction transaction)
        {
            string query = "SELECT COUNT(*) FROM Product WHERE Id = @ProductId";
            using (SqlCommand command = new SqlCommand(query, connection, transaction))
            {
                command.Parameters.AddWithValue("@ProductId", productId);
                return (int)await command.ExecuteScalarAsync() > 0;
            }
        }

        private async Task<bool> WarehouseExists(int warehouseId, SqlConnection connection, SqlTransaction transaction)
        {
            string query = "SELECT COUNT(*) FROM Warehouse WHERE Id = @WarehouseId";
            using (SqlCommand command = new SqlCommand(query, connection, transaction))
            {
                command.Parameters.AddWithValue("@WarehouseId", warehouseId);
                return (int)await command.ExecuteScalarAsync() > 0;
            }
        }

        private async Task<bool> OrderExistsAndValid(int productId, int amount, DateTime createdAt, SqlConnection connection, SqlTransaction transaction)
        {
            string query = @"
                SELECT COUNT(*)
                FROM [Order]
                WHERE ProductId = @ProductId AND Amount = @Amount AND CreatedAt < @CreatedAt";
            using (SqlCommand command = new SqlCommand(query, connection, transaction))
            {
                command.Parameters.AddWithValue("@ProductId", productId);
                command.Parameters.AddWithValue("@Amount", amount);
                command.Parameters.AddWithValue("@CreatedAt", createdAt);
                return (int)await command.ExecuteScalarAsync() > 0;
            }
        }

        private async Task UpdateOrder(int productId, int amount, SqlConnection connection, SqlTransaction transaction)
        {
            string query = @"
                UPDATE [Order]
                SET FulfilledAt = @FulfilledAt
                WHERE ProductId = @ProductId AND Amount = @Amount";
            using (SqlCommand command = new SqlCommand(query, connection, transaction))
            {
                command.Parameters.AddWithValue("@FulfilledAt", DateTime.Now);
                command.Parameters.AddWithValue("@ProductId", productId);
                command.Parameters.AddWithValue("@Amount", amount);
                await command.ExecuteNonQueryAsync();
            }
        }

        private async Task<int> InsertProductWarehouseRecord(int productId, int warehouseId, int amount, DateTime createdAt, SqlConnection connection, SqlTransaction transaction)
        {
            string query = @"
                INSERT INTO Product_Warehouse (ProductId, WarehouseId, Amount, Price, CreatedAt)
                VALUES (@ProductId, @WarehouseId, @Amount, @Price, @CreatedAt);
                SELECT CAST(scope_identity() AS int)";
            using (SqlCommand command = new SqlCommand(query, connection, transaction))
            {
                command.Parameters.AddWithValue("@ProductId", productId);
                command.Parameters.AddWithValue("@WarehouseId", warehouseId);
                command.Parameters.AddWithValue("@Amount", amount);
                command.Parameters.AddWithValue("@Price", await GetProductPrice(productId, connection, transaction) * amount);
                command.Parameters.AddWithValue("@CreatedAt", DateTime.Now);
                return (int)await command.ExecuteScalarAsync();
            }
        }

        private async Task<decimal> GetProductPrice(int productId, SqlConnection connection, SqlTransaction transaction)
        {
            string query = "SELECT Price FROM Product WHERE Id = @ProductId";
            using (SqlCommand command = new SqlCommand(query, connection, transaction))
            {
                command.Parameters.AddWithValue("@ProductId", productId);
                return (decimal)await command.ExecuteScalarAsync();
            }
        }
    }
}
