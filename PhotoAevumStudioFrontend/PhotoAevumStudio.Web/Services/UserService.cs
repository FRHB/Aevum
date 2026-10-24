using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using PhotoAevumStudio.Web.Models;

namespace PhotoAevumStudio.Web.Services;

public sealed class UserService : IUserService
{
	private const string DefaultLocalConnectionString = "Data Source=localhost\\SQLEXPRESS;Initial Catalog=PhotoAevumStudio;Integrated Security=True;Persist Security Info=False;Pooling=False;MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=True;Application Name=\"SQL Server Management Studio\";Command Timeout=0";

	private static readonly Regex ValidAdminIdRegex = new Regex("^(SADM|ADM)-\\d{3}$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

	private static readonly Regex ValidEmployeeIdRegex = new Regex("^EMP-\\d{3}$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

	private static readonly Regex ValidCustomerIdRegex = new Regex("^CUST-\\d{4}$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

	private readonly string _connectionString;

	private readonly IHttpClientFactory? _httpClientFactory;

	public UserService(IConfiguration? configuration = null, IHttpClientFactory? httpClientFactory = null)
	{
		_connectionString = configuration?.GetConnectionString("DefaultConnection") ?? "Data Source=localhost\\SQLEXPRESS;Initial Catalog=PhotoAevumStudio;Integrated Security=True;Persist Security Info=False;Pooling=False;MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=True;Application Name=\"SQL Server Management Studio\";Command Timeout=0";
		_httpClientFactory = httpClientFactory;
	}

	private SqlConnection CreateConnection()
	{
		return new SqlConnection(_connectionString);
	}

	private static (string FirstName, string? MiddleName, string LastName) SplitFullName(string fullName)
	{
		string[] array = (fullName ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
		int num = array.Length;
		if (1 == 0)
		{
		}
		(string, string, string) result = num switch
		{
			0 => ("User", null, "Account"), 
			1 => (array[0], null, array[0]), 
			2 => (array[0], null, array[1]), 
			_ => (array[0], string.Join(" ", array[1..^1]), array[^1]), 
		};
		if (1 == 0)
		{
		}
		return result;
	}

	public IReadOnlyList<Admin> GetAdmins()
	{
		List<Admin> list = new List<Admin>();
		using SqlConnection sqlConnection = CreateConnection();
		sqlConnection.Open();
		bool flag = false;
		using (SqlCommand sqlCommand = new SqlCommand("SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Admin' AND COLUMN_NAME = 'Email';", sqlConnection))
		{
			flag = sqlCommand.ExecuteScalar() != null;
		}
		string cmdText = (flag ? "IF OBJECT_ID(N'dbo.Admin', N'U') IS NOT NULL\n                BEGIN\n                    SELECT AdminID, AdminRole, AdminFirstName, AdminLastName, AdminMiddleName, Email\n                    FROM dbo.Admin\n                    ORDER BY AdminID;\n                END" : "IF OBJECT_ID(N'dbo.Admin', N'U') IS NOT NULL\n                BEGIN\n                    SELECT AdminID, AdminRole, AdminFirstName, AdminLastName, AdminMiddleName, NULL AS Email\n                    FROM dbo.Admin\n                    ORDER BY AdminID;\n                END");
		using SqlCommand sqlCommand2 = new SqlCommand(cmdText, sqlConnection);
		using SqlDataReader sqlDataReader = sqlCommand2.ExecuteReader();
		while (sqlDataReader.Read())
		{
			string text = sqlDataReader.GetString(0).Trim().ToUpperInvariant();
			if (ValidAdminIdRegex.IsMatch(text))
			{
				string text2 = (sqlDataReader.IsDBNull(1) ? "Admin" : sqlDataReader.GetString(1));
				string text3 = (sqlDataReader.IsDBNull(2) ? string.Empty : sqlDataReader.GetString(2));
				string text4 = (sqlDataReader.IsDBNull(3) ? string.Empty : sqlDataReader.GetString(3));
				string text5 = (sqlDataReader.IsDBNull(4) ? null : sqlDataReader.GetString(4));
				string text6 = (sqlDataReader.IsDBNull(5) ? (text.ToLowerInvariant() + "@photoaevum.studio") : sqlDataReader.GetString(5));
				string name = string.Join(" ", new string[3] { text3, text5, text4 }.Where((string s) => !string.IsNullOrWhiteSpace(s)));
				list.Add(new Admin
				{
					Id = text,
					Name = name,
					Email = (string.IsNullOrWhiteSpace(text6) ? (text.ToLowerInvariant() + "@photoaevum.studio") : text6),
					Role = (text.StartsWith("SADM-", StringComparison.OrdinalIgnoreCase) ? "Super Admin" : text2)
				});
			}
		}
		return list;
	}

	public Admin? GetAdminById(string id)
	{
		if (string.IsNullOrWhiteSpace(id))
		{
			return null;
		}
		string value = id.Trim().ToUpperInvariant();
		using SqlConnection sqlConnection = CreateConnection();
		sqlConnection.Open();
		bool flag = false;
		using (SqlCommand sqlCommand = new SqlCommand("SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Admin' AND COLUMN_NAME = 'Email';", sqlConnection))
		{
			flag = sqlCommand.ExecuteScalar() != null;
		}
		string cmdText = (flag ? "IF OBJECT_ID(N'dbo.Admin', N'U') IS NOT NULL\n                BEGIN\n                    SELECT AdminID, AdminRole, AdminFirstName, AdminLastName, AdminMiddleName, Email\n                    FROM dbo.Admin\n                    WHERE UPPER(AdminID) = @Id;\n                END" : "IF OBJECT_ID(N'dbo.Admin', N'U') IS NOT NULL\n                BEGIN\n                    SELECT AdminID, AdminRole, AdminFirstName, AdminLastName, AdminMiddleName, NULL AS Email\n                    FROM dbo.Admin\n                    WHERE UPPER(AdminID) = @Id;\n                END");
		using SqlCommand sqlCommand2 = new SqlCommand(cmdText, sqlConnection);
		sqlCommand2.Parameters.AddWithValue("@Id", value);
		using SqlDataReader sqlDataReader = sqlCommand2.ExecuteReader();
		if (!sqlDataReader.Read())
		{
			return null;
		}
		string text = sqlDataReader.GetString(0).Trim().ToUpperInvariant();
		if (!ValidAdminIdRegex.IsMatch(text))
		{
			return null;
		}
		string text2 = (sqlDataReader.IsDBNull(1) ? "Admin" : sqlDataReader.GetString(1));
		string text3 = (sqlDataReader.IsDBNull(2) ? string.Empty : sqlDataReader.GetString(2));
		string text4 = (sqlDataReader.IsDBNull(3) ? string.Empty : sqlDataReader.GetString(3));
		string text5 = (sqlDataReader.IsDBNull(4) ? null : sqlDataReader.GetString(4));
		string text6 = (sqlDataReader.IsDBNull(5) ? (text.ToLowerInvariant() + "@photoaevum.studio") : sqlDataReader.GetString(5));
		string name = string.Join(" ", new string[3] { text3, text5, text4 }.Where((string s) => !string.IsNullOrWhiteSpace(s)));
		return new Admin
		{
			Id = text,
			Name = name,
			Email = (string.IsNullOrWhiteSpace(text6) ? (text.ToLowerInvariant() + "@photoaevum.studio") : text6),
			Role = (text.StartsWith("SADM-", StringComparison.OrdinalIgnoreCase) ? "Super Admin" : text2)
		};
	}

	public void AddAdmin(Admin admin, string? requestingUserId = null)
	{
		ArgumentNullException.ThrowIfNull(admin, "admin");
		if (!string.IsNullOrWhiteSpace(requestingUserId) && !requestingUserId.StartsWith("SADM-", StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException("Insufficient privileges. Only a System Administrator (SADM-###) is authorized to create a new Admin account.");
		}
		var (value, text, value2) = SplitFullName(admin.Name);
		using SqlConnection sqlConnection = CreateConnection();
		sqlConnection.Open();
		if (GetAdminById(admin.Id) != null)
		{
			throw new InvalidOperationException("An admin with ID '" + admin.Id + "' already exists.");
		}
		bool flag = false;
		using (SqlCommand sqlCommand = new SqlCommand("SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Admin' AND COLUMN_NAME = 'Email';", sqlConnection))
		{
			flag = sqlCommand.ExecuteScalar() != null;
		}
		string value3 = (string.IsNullOrWhiteSpace(admin.Email) ? (admin.Id.ToLowerInvariant() + "@photoaevum.studio") : admin.Email.Trim());
		string cmdText = (flag ? "INSERT INTO dbo.Admin (AdminID, AdminRole, AdminFirstName, AdminLastName, AdminMiddleName, Email, AdminPassword, isFirstLogin)\n                VALUES (@Id, @Role, @First, @Last, @Middle, @Email, @Password, 1);" : "INSERT INTO dbo.Admin (AdminID, AdminRole, AdminFirstName, AdminLastName, AdminMiddleName, AdminPassword, isFirstLogin)\n                VALUES (@Id, @Role, @First, @Last, @Middle, @Password, 1);");
		using SqlCommand sqlCommand2 = new SqlCommand(cmdText, sqlConnection);
		sqlCommand2.Parameters.AddWithValue("@Id", admin.Id.ToUpperInvariant());
		sqlCommand2.Parameters.AddWithValue("@Role", string.IsNullOrWhiteSpace(admin.Role) ? "Admin" : admin.Role);
		sqlCommand2.Parameters.AddWithValue("@First", value);
		sqlCommand2.Parameters.AddWithValue("@Last", value2);
		sqlCommand2.Parameters.AddWithValue("@Middle", ((object)text) ?? ((object)DBNull.Value));
		if (flag)
		{
			sqlCommand2.Parameters.AddWithValue("@Email", value3);
		}
		sqlCommand2.Parameters.AddWithValue("@Password", "AxiomBit@2026");
		sqlCommand2.ExecuteNonQuery();
		try
		{
			using SqlCommand sqlCommand3 = new SqlCommand("\n                IF OBJECT_ID(N'dbo.HistoryLog', N'U') IS NOT NULL\n                BEGIN\n                    INSERT INTO dbo.HistoryLog (ActionType, EntityName, EntityID, ChangedBy, ActionDetails, LoggedAt)\n                    VALUES ('Create', 'Admin', @Id, @By, @Details, SYSUTCDATETIME());\n                END", sqlConnection);
			sqlCommand3.Parameters.AddWithValue("@Id", admin.Id.ToUpperInvariant());
			sqlCommand3.Parameters.AddWithValue("@By", string.IsNullOrWhiteSpace(requestingUserId) ? "SADM-001" : requestingUserId.Trim());
			sqlCommand3.Parameters.AddWithValue("@Details", $"Created admin {admin.Id} ({admin.Name}) with email {value3}");
			sqlCommand3.ExecuteNonQuery();
		}
		catch
		{
		}
	}

	public void DeleteAdmin(string targetId, string requestingUserId)
	{
		if (targetId.StartsWith("SADM-", StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException("The Super Admin account (SADM-###) is the root administrator and can never be deleted or modified.");
		}
		if (!requestingUserId.StartsWith("SADM-", StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException("Insufficient privileges. Only the Super Admin (SADM-###) can delete a regular Admin account.");
		}
		Admin admin = GetAdminById(targetId) ?? throw new KeyNotFoundException("Admin with ID '" + targetId + "' was not found.");
		using SqlConnection sqlConnection = CreateConnection();
		sqlConnection.Open();
		using SqlCommand sqlCommand = new SqlCommand("DELETE FROM dbo.Admin WHERE UPPER(AdminID) = @Id;", sqlConnection);
		sqlCommand.Parameters.AddWithValue("@Id", admin.Id.ToUpperInvariant());
		sqlCommand.ExecuteNonQuery();
		try
		{
			using SqlCommand sqlCommand2 = new SqlCommand("\n                IF OBJECT_ID(N'dbo.HistoryLog', N'U') IS NOT NULL\n                BEGIN\n                    INSERT INTO dbo.HistoryLog (ActionType, EntityName, EntityID, ChangedBy, ActionDetails, LoggedAt)\n                    VALUES ('Delete', 'Admin', @Id, @By, 'Deleted admin account', SYSUTCDATETIME());\n                END", sqlConnection);
			sqlCommand2.Parameters.AddWithValue("@Id", admin.Id.ToUpperInvariant());
			sqlCommand2.Parameters.AddWithValue("@By", requestingUserId.Trim());
			sqlCommand2.ExecuteNonQuery();
		}
		catch
		{
		}
	}

	public IReadOnlyList<Employee> GetEmployees()
	{
		List<Employee> list = new List<Employee>();
		using SqlConnection sqlConnection = CreateConnection();
		sqlConnection.Open();
		using SqlCommand sqlCommand = new SqlCommand("\n            IF OBJECT_ID(N'dbo.Employee', N'U') IS NOT NULL\n            BEGIN\n                SELECT e.EmployeeID,\n                       e.EmployeeFirstName,\n                       e.EmployeeLastName,\n                       e.EmployeeMiddleName,\n                       e.EmployeeEmail,\n                       (\n                           SELECT TOP 1 COALESCE(NULLIF(es.SpecializationName, ''), es.Specialization)\n                           FROM dbo.EmployeeJointSpecialization ejs\n                           INNER JOIN dbo.EmployeeSpecialization es ON es.SpecializationID = ejs.SpecializationID\n                           WHERE ejs.EmployeeID = e.EmployeeID\n                       ) AS PrimarySpecialization,\n                       (\n                           SELECT TOP 1 COALESCE(NULLIF(es.SpecializationDescription, ''), es.Description)\n                           FROM dbo.EmployeeJointSpecialization ejs\n                           INNER JOIN dbo.EmployeeSpecialization es ON es.SpecializationID = ejs.SpecializationID\n                           WHERE ejs.EmployeeID = e.EmployeeID\n                       ) AS PrimaryDepartment\n                FROM dbo.Employee e\n                ORDER BY e.EmployeeID;\n            END", sqlConnection);
		using SqlDataReader sqlDataReader = sqlCommand.ExecuteReader();
		while (sqlDataReader.Read())
		{
			string text = sqlDataReader.GetString(0).Trim().ToUpperInvariant();
			if (ValidEmployeeIdRegex.IsMatch(text))
			{
				string text2 = (sqlDataReader.IsDBNull(1) ? string.Empty : sqlDataReader.GetString(1));
				string text3 = (sqlDataReader.IsDBNull(2) ? string.Empty : sqlDataReader.GetString(2));
				string text4 = (sqlDataReader.IsDBNull(3) ? null : sqlDataReader.GetString(3));
				string email = (sqlDataReader.IsDBNull(4) ? string.Empty : sqlDataReader.GetString(4));
				string position = (sqlDataReader.IsDBNull(5) ? string.Empty : sqlDataReader.GetString(5));
				string department = (sqlDataReader.IsDBNull(6) ? string.Empty : sqlDataReader.GetString(6));
				string name = string.Join(" ", new string[3] { text2, text4, text3 }.Where((string s) => !string.IsNullOrWhiteSpace(s)));
				list.Add(new Employee
				{
					Id = text,
					Name = name,
					Email = email,
					Position = position,
					Department = department
				});
			}
		}
		return list;
	}

	public Employee? GetEmployeeById(string id)
	{
		if (string.IsNullOrWhiteSpace(id))
		{
			return null;
		}
		string value = id.Trim().ToUpperInvariant();
		using SqlConnection sqlConnection = CreateConnection();
		sqlConnection.Open();
		using SqlCommand sqlCommand = new SqlCommand("\n            IF OBJECT_ID(N'dbo.Employee', N'U') IS NOT NULL\n            BEGIN\n                SELECT e.EmployeeID,\n                       e.EmployeeFirstName,\n                       e.EmployeeLastName,\n                       e.EmployeeMiddleName,\n                       e.EmployeeEmail,\n                       (\n                           SELECT TOP 1 COALESCE(NULLIF(es.SpecializationName, ''), es.Specialization)\n                           FROM dbo.EmployeeJointSpecialization ejs\n                           INNER JOIN dbo.EmployeeSpecialization es ON es.SpecializationID = ejs.SpecializationID\n                           WHERE ejs.EmployeeID = e.EmployeeID\n                       ) AS PrimarySpecialization,\n                       (\n                           SELECT TOP 1 COALESCE(NULLIF(es.SpecializationDescription, ''), es.Description)\n                           FROM dbo.EmployeeJointSpecialization ejs\n                           INNER JOIN dbo.EmployeeSpecialization es ON es.SpecializationID = ejs.SpecializationID\n                           WHERE ejs.EmployeeID = e.EmployeeID\n                       ) AS PrimaryDepartment\n                FROM dbo.Employee e\n                WHERE UPPER(e.EmployeeID) = @Id;\n            END", sqlConnection);
		sqlCommand.Parameters.AddWithValue("@Id", value);
		using SqlDataReader sqlDataReader = sqlCommand.ExecuteReader();
		if (!sqlDataReader.Read())
		{
			return null;
		}
		string text = sqlDataReader.GetString(0).Trim().ToUpperInvariant();
		if (!ValidEmployeeIdRegex.IsMatch(text))
		{
			return null;
		}
		string text2 = (sqlDataReader.IsDBNull(1) ? string.Empty : sqlDataReader.GetString(1));
		string text3 = (sqlDataReader.IsDBNull(2) ? string.Empty : sqlDataReader.GetString(2));
		string text4 = (sqlDataReader.IsDBNull(3) ? null : sqlDataReader.GetString(3));
		string email = (sqlDataReader.IsDBNull(4) ? string.Empty : sqlDataReader.GetString(4));
		string position = (sqlDataReader.IsDBNull(5) ? string.Empty : sqlDataReader.GetString(5));
		string department = (sqlDataReader.IsDBNull(6) ? string.Empty : sqlDataReader.GetString(6));
		string name = string.Join(" ", new string[3] { text2, text4, text3 }.Where((string s) => !string.IsNullOrWhiteSpace(s)));
		return new Employee
		{
			Id = text,
			Name = name,
			Email = email,
			Position = position,
			Department = department
		};
	}

	public void AddEmployee(Employee employee)
	{
		ArgumentNullException.ThrowIfNull(employee, "employee");
		if (GetEmployeeById(employee.Id) != null)
		{
			throw new InvalidOperationException("An employee with ID '" + employee.Id + "' already exists.");
		}
		var (value, text, value2) = SplitFullName(employee.Name);
		using SqlConnection sqlConnection = CreateConnection();
		sqlConnection.Open();
		using (SqlCommand sqlCommand = new SqlCommand("\n            INSERT INTO dbo.Employee (EmployeeID, EmployeeFirstName, EmployeeLastName, EmployeeMiddleName, EmployeeEmail, EmployeePassword, EmployeeStatus, isFirstLogin)\n            VALUES (@Id, @First, @Last, @Middle, @Email, @Password, 'Active', 1);", sqlConnection))
		{
			sqlCommand.Parameters.AddWithValue("@Id", employee.Id.ToUpperInvariant());
			sqlCommand.Parameters.AddWithValue("@First", value);
			sqlCommand.Parameters.AddWithValue("@Last", value2);
			sqlCommand.Parameters.AddWithValue("@Middle", ((object)text) ?? ((object)DBNull.Value));
			sqlCommand.Parameters.AddWithValue("@Email", string.IsNullOrWhiteSpace(employee.Email) ? (employee.Id.ToLowerInvariant() + "@photoaevum.studio") : employee.Email);
			sqlCommand.Parameters.AddWithValue("@Password", "AxiomBit@2026");
			sqlCommand.ExecuteNonQuery();
		}
		if (!string.IsNullOrWhiteSpace(employee.Position) || !string.IsNullOrWhiteSpace(employee.Department))
		{
			string value3 = "SP-" + employee.Id.Replace("EMP-", "", StringComparison.OrdinalIgnoreCase);
			string value4 = "EJS-" + employee.Id.Replace("EMP-", "", StringComparison.OrdinalIgnoreCase);
			using SqlCommand sqlCommand2 = new SqlCommand("\n                IF NOT EXISTS (SELECT 1 FROM dbo.EmployeeSpecialization WHERE SpecializationID = @SpecId)\n                BEGIN\n                    INSERT INTO dbo.EmployeeSpecialization (SpecializationID, SpecializationName, SpecializationDescription, Specialization, Description, ColorTag, CreatedAt)\n                    VALUES (@SpecId, @Position, @Department, @Position, @Department, 'Blue', SYSUTCDATETIME());\n                END\n                ELSE\n                BEGIN\n                    UPDATE dbo.EmployeeSpecialization\n                    SET SpecializationName = @Position, SpecializationDescription = @Department, Specialization = @Position, Description = @Department\n                    WHERE SpecializationID = @SpecId;\n                END\n\n                IF NOT EXISTS (SELECT 1 FROM dbo.EmployeeJointSpecialization WHERE EmployeeJointSpecializationID = @JointId)\n                BEGIN\n                    INSERT INTO dbo.EmployeeJointSpecialization (EmployeeJointSpecializationID, SpecializationID, EmployeeID, AssignedAt)\n                    VALUES (@JointId, @SpecId, @EmpId, SYSUTCDATETIME());\n                END", sqlConnection);
			sqlCommand2.Parameters.AddWithValue("@SpecId", value3);
			sqlCommand2.Parameters.AddWithValue("@JointId", value4);
			sqlCommand2.Parameters.AddWithValue("@EmpId", employee.Id.ToUpperInvariant());
			sqlCommand2.Parameters.AddWithValue("@Position", employee.Position ?? string.Empty);
			sqlCommand2.Parameters.AddWithValue("@Department", employee.Department ?? string.Empty);
			sqlCommand2.ExecuteNonQuery();
			return;
		}
	}

	public bool DeleteEmployee(string id)
	{
		if (string.IsNullOrWhiteSpace(id))
		{
			return false;
		}
		using SqlConnection sqlConnection = CreateConnection();
		sqlConnection.Open();
		using SqlCommand sqlCommand = new SqlCommand("\n            DELETE FROM dbo.EmployeeJointSpecialization WHERE UPPER(EmployeeID) = @Id;\n            DELETE FROM dbo.Employee WHERE UPPER(EmployeeID) = @Id;", sqlConnection);
		sqlCommand.Parameters.AddWithValue("@Id", id.Trim().ToUpperInvariant());
		return sqlCommand.ExecuteNonQuery() > 0;
	}

	public IReadOnlyList<Customer> GetCustomers()
	{
		List<Customer> list = new List<Customer>();
		using SqlConnection sqlConnection = CreateConnection();
		sqlConnection.Open();
		using SqlCommand sqlCommand = new SqlCommand("\n            IF OBJECT_ID(N'dbo.Customer', N'U') IS NOT NULL\n            BEGIN\n                SELECT CustomerID, CustomerFirstName, CustomerLastName, CustomerMiddleName, CustomerEmail, CustomerPhoneNumber\n                FROM dbo.Customer\n                ORDER BY CustomerID;\n            END", sqlConnection);
		using SqlDataReader sqlDataReader = sqlCommand.ExecuteReader();
		while (sqlDataReader.Read())
		{
			string text = sqlDataReader.GetString(0).Trim().ToUpperInvariant();
			if (ValidCustomerIdRegex.IsMatch(text))
			{
				string text2 = (sqlDataReader.IsDBNull(1) ? string.Empty : sqlDataReader.GetString(1));
				string text3 = (sqlDataReader.IsDBNull(2) ? string.Empty : sqlDataReader.GetString(2));
				string text4 = (sqlDataReader.IsDBNull(3) ? null : sqlDataReader.GetString(3));
				string email = (sqlDataReader.IsDBNull(4) ? string.Empty : sqlDataReader.GetString(4));
				string phone = (sqlDataReader.IsDBNull(5) ? string.Empty : sqlDataReader.GetString(5));
				string name = string.Join(" ", new string[3] { text2, text4, text3 }.Where((string s) => !string.IsNullOrWhiteSpace(s)));
				list.Add(new Customer
				{
					Id = text,
					Name = name,
					Email = email,
					Phone = phone
				});
			}
		}
		return list;
	}

	public Customer? GetCustomerById(string id)
	{
		if (string.IsNullOrWhiteSpace(id))
		{
			return null;
		}
		string value = id.Trim().ToUpperInvariant();
		using SqlConnection sqlConnection = CreateConnection();
		sqlConnection.Open();
		using SqlCommand sqlCommand = new SqlCommand("\n            IF OBJECT_ID(N'dbo.Customer', N'U') IS NOT NULL\n            BEGIN\n                SELECT CustomerID, CustomerFirstName, CustomerLastName, CustomerMiddleName, CustomerEmail, CustomerPhoneNumber\n                FROM dbo.Customer\n                WHERE UPPER(CustomerID) = @Id OR UPPER(CustomerEmail) = @Id;\n            END", sqlConnection);
		sqlCommand.Parameters.AddWithValue("@Id", value);
		using SqlDataReader sqlDataReader = sqlCommand.ExecuteReader();
		if (!sqlDataReader.Read())
		{
			return null;
		}
		string text = sqlDataReader.GetString(0).Trim().ToUpperInvariant();
		if (!ValidCustomerIdRegex.IsMatch(text))
		{
			return null;
		}
		string text2 = (sqlDataReader.IsDBNull(1) ? string.Empty : sqlDataReader.GetString(1));
		string text3 = (sqlDataReader.IsDBNull(2) ? string.Empty : sqlDataReader.GetString(2));
		string text4 = (sqlDataReader.IsDBNull(3) ? null : sqlDataReader.GetString(3));
		string email = (sqlDataReader.IsDBNull(4) ? string.Empty : sqlDataReader.GetString(4));
		string phone = (sqlDataReader.IsDBNull(5) ? string.Empty : sqlDataReader.GetString(5));
		string name = string.Join(" ", new string[3] { text2, text4, text3 }.Where((string s) => !string.IsNullOrWhiteSpace(s)));
		return new Customer
		{
			Id = text,
			Name = name,
			Email = email,
			Phone = phone
		};
	}

	public void AddCustomer(Customer customer)
	{
		ArgumentNullException.ThrowIfNull(customer, "customer");
		if (GetCustomerById(customer.Id) != null)
		{
			throw new InvalidOperationException("A customer with ID '" + customer.Id + "' already exists.");
		}
		var (value, text, value2) = SplitFullName(customer.Name);
		using SqlConnection sqlConnection = CreateConnection();
		sqlConnection.Open();
		using SqlCommand sqlCommand = new SqlCommand("\n            INSERT INTO dbo.Customer (CustomerID, CustomerFirstName, CustomerLastName, CustomerMiddleName, CustomerEmail, CustomerPhoneNumber, CustomerPassword)\n            VALUES (@Id, @First, @Last, @Middle, @Email, @Phone, @Password);", sqlConnection);
		sqlCommand.Parameters.AddWithValue("@Id", customer.Id.ToUpperInvariant());
		sqlCommand.Parameters.AddWithValue("@First", value);
		sqlCommand.Parameters.AddWithValue("@Last", value2);
		sqlCommand.Parameters.AddWithValue("@Middle", ((object)text) ?? ((object)DBNull.Value));
		sqlCommand.Parameters.AddWithValue("@Email", customer.Email ?? string.Empty);
		sqlCommand.Parameters.AddWithValue("@Phone", string.IsNullOrWhiteSpace(customer.Phone) ? ((IConvertible)DBNull.Value) : ((IConvertible)customer.Phone));
		sqlCommand.Parameters.AddWithValue("@Password", "AxiomBit@2026");
		sqlCommand.ExecuteNonQuery();
	}

	public bool DeleteCustomer(string id)
	{
		if (string.IsNullOrWhiteSpace(id))
		{
			return false;
		}
		using SqlConnection sqlConnection = CreateConnection();
		sqlConnection.Open();
		using SqlCommand sqlCommand = new SqlCommand("DELETE FROM dbo.Customer WHERE UPPER(CustomerID) = @Id;", sqlConnection);
		sqlCommand.Parameters.AddWithValue("@Id", id.Trim().ToUpperInvariant());
		return sqlCommand.ExecuteNonQuery() > 0;
	}

	public IReadOnlyList<SpecializationLookupItem> GetSpecializationLookups()
	{
		List<SpecializationLookupItem> list = new List<SpecializationLookupItem>();
		using SqlConnection sqlConnection = CreateConnection();
		sqlConnection.Open();
		using SqlCommand sqlCommand = new SqlCommand("\n            IF OBJECT_ID(N'dbo.EmployeeSpecialization', N'U') IS NOT NULL\n            BEGIN\n                SELECT SpecializationID,\n                       COALESCE(NULLIF(SpecializationName, ''), Specialization, SpecializationID) AS Name,\n                       COALESCE(NULLIF(SpecializationDescription, ''), Description) AS Description,\n                       COALESCE(NULLIF(ColorTag, ''), 'Blue') AS ColorTag,\n                       COALESCE(CreatedAt, SYSUTCDATETIME()) AS CreatedAt\n                FROM dbo.EmployeeSpecialization\n                ORDER BY SpecializationID;\n            END", sqlConnection);
		using SqlDataReader sqlDataReader = sqlCommand.ExecuteReader();
		while (sqlDataReader.Read())
		{
			list.Add(new SpecializationLookupItem
			{
				Id = sqlDataReader.GetString(0),
				Name = sqlDataReader.GetString(1),
				Description = (sqlDataReader.IsDBNull(2) ? null : sqlDataReader.GetString(2)),
				Department = "Photography",
				ColorTag = (sqlDataReader.IsDBNull(3) ? "Blue" : sqlDataReader.GetString(3)),
				CreatedAt = (sqlDataReader.IsDBNull(4) ? DateTime.UtcNow : sqlDataReader.GetDateTime(4))
			});
		}
		return list;
	}

	public IReadOnlyList<EmployeeSpecializationDto> GetEmployeeSpecializations()
	{
		List<EmployeeSpecializationDto> list = new List<EmployeeSpecializationDto>();
		using SqlConnection sqlConnection = CreateConnection();
		sqlConnection.Open();
		Dictionary<string, List<string>> dictionary = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
		using (SqlCommand sqlCommand = new SqlCommand("\n            IF OBJECT_ID(N'dbo.EmployeeJointSpecialization', N'U') IS NOT NULL\n               AND OBJECT_ID(N'dbo.EmployeeSpecialization', N'U') IS NOT NULL\n            BEGIN\n                SELECT ejs.EmployeeID, COALESCE(NULLIF(es.SpecializationName, ''), es.Specialization, es.SpecializationID)\n                FROM dbo.EmployeeJointSpecialization ejs\n                INNER JOIN dbo.EmployeeSpecialization es ON es.SpecializationID = ejs.SpecializationID;\n            END", sqlConnection))
		{
			using SqlDataReader sqlDataReader = sqlCommand.ExecuteReader();
			while (sqlDataReader.Read())
			{
				string key = sqlDataReader.GetString(0);
				string text = (sqlDataReader.IsDBNull(1) ? string.Empty : sqlDataReader.GetString(1));
				if (!string.IsNullOrWhiteSpace(text))
				{
					if (!dictionary.TryGetValue(key, out var value))
					{
						value = (dictionary[key] = new List<string>());
					}
					if (!value.Contains(text, StringComparer.OrdinalIgnoreCase))
					{
						value.Add(text);
					}
				}
			}
		}
		using SqlCommand sqlCommand2 = new SqlCommand("\n            IF OBJECT_ID(N'dbo.Employee', N'U') IS NOT NULL\n            BEGIN\n                SELECT EmployeeID, EmployeeFirstName, EmployeeLastName, EmployeeMiddleName, EmployeeEmail, EmployeeStatus\n                FROM dbo.Employee\n                ORDER BY EmployeeID;\n            END", sqlConnection);
		using SqlDataReader sqlDataReader2 = sqlCommand2.ExecuteReader();
		int num = 1;
		while (sqlDataReader2.Read())
		{
			string text2 = sqlDataReader2.GetString(0);
			dictionary.TryGetValue(text2, out var value2);
			list.Add(new EmployeeSpecializationDto
			{
				Id = num++,
				EmployeeId = text2,
				FirstName = (sqlDataReader2.IsDBNull(1) ? string.Empty : sqlDataReader2.GetString(1)),
				LastName = (sqlDataReader2.IsDBNull(2) ? string.Empty : sqlDataReader2.GetString(2)),
				MiddleName = (sqlDataReader2.IsDBNull(3) ? null : sqlDataReader2.GetString(3)),
				Email = (sqlDataReader2.IsDBNull(4) ? string.Empty : sqlDataReader2.GetString(4)),
				Role = "Employee",
				Status = (sqlDataReader2.IsDBNull(5) ? "Active" : sqlDataReader2.GetString(5)),
				Specializations = (value2 ?? new List<string>())
			});
		}
		return list;
	}

	public SpecializationLookupItem AddSpecialization(SpecializationModel model)
	{
		ArgumentNullException.ThrowIfNull(model, "model");
		using SqlConnection sqlConnection = CreateConnection();
		sqlConnection.Open();
		int count = GetSpecializationLookups().Count;
		string text = $"SP-{count + 1:D3}";
		using SqlCommand sqlCommand = new SqlCommand("\n            INSERT INTO dbo.EmployeeSpecialization (SpecializationID, SpecializationName, SpecializationDescription, Specialization, Description, ColorTag, CreatedAt)\n            VALUES (@Id, @Name, @Desc, @Name, @Desc, @Color, SYSUTCDATETIME());", sqlConnection);
		sqlCommand.Parameters.AddWithValue("@Id", text);
		sqlCommand.Parameters.AddWithValue("@Name", model.Name.Trim());
		sqlCommand.Parameters.AddWithValue("@Desc", ((object)model.Description?.Trim()) ?? ((object)DBNull.Value));
		sqlCommand.Parameters.AddWithValue("@Color", string.IsNullOrWhiteSpace(model.ColorTag) ? "Blue" : model.ColorTag);
		sqlCommand.ExecuteNonQuery();
		return new SpecializationLookupItem
		{
			Id = text,
			Name = model.Name.Trim(),
			Description = model.Description?.Trim(),
			Department = model.Department,
			ColorTag = model.ColorTag,
			CreatedAt = DateTime.UtcNow
		};
	}

	public IReadOnlyList<MainServiceItem> GetMainServices()
	{
		List<MainServiceItem> list = new List<MainServiceItem>();
		using SqlConnection sqlConnection = CreateConnection();
		sqlConnection.Open();
		using SqlCommand sqlCommand = new SqlCommand("\n            IF OBJECT_ID(N'dbo.Services', N'U') IS NOT NULL\n            BEGIN\n                SELECT ServicesID, ServiceName, ServicesDescription\n                FROM dbo.Services\n                ORDER BY ServicesID;\n            END", sqlConnection);
		using SqlDataReader sqlDataReader = sqlCommand.ExecuteReader();
		while (sqlDataReader.Read())
		{
			string code = sqlDataReader.GetString(0);
			string title = sqlDataReader.GetString(1);
			string description = (sqlDataReader.IsDBNull(2) ? null : sqlDataReader.GetString(2));
			list.Add(new MainServiceItem
			{
				Code = code,
				Title = title,
				Category = "Studio",
				CategorySpec = "Photography",
				Deliverables = new List<(string, string)>(),
				Duration = "1 hr",
				BasePrice = 0m,
				Description = description,
				Status = "Active"
			});
		}
		return list;
	}

	public IReadOnlyList<MainServiceItem> GetMainServicesSummary()
	{
		List<MainServiceItem> list = new List<MainServiceItem>();
		try
		{
			using SqlConnection sqlConnection = CreateConnection();
			sqlConnection.Open();
			using SqlCommand sqlCommand = new SqlCommand("\n                IF OBJECT_ID(N'dbo.sp_GetServicesSummary', N'P') IS NOT NULL\n                BEGIN\n                    EXEC dbo.sp_GetServicesSummary;\n                END\n                ELSE IF OBJECT_ID(N'dbo.Services', N'U') IS NOT NULL\n                BEGIN\n                    SELECT ServicesID, ServiceName, ServicesDescription\n                    FROM dbo.Services\n                    ORDER BY ServicesID;\n                END", sqlConnection);
			using SqlDataReader sqlDataReader = sqlCommand.ExecuteReader();
			while (sqlDataReader.Read())
			{
				string code = sqlDataReader.GetString(0);
				string title = sqlDataReader.GetString(1);
				string description = (sqlDataReader.IsDBNull(2) ? null : sqlDataReader.GetString(2));
				list.Add(new MainServiceItem
				{
					Code = code,
					Title = title,
					Category = "Studio",
					CategorySpec = "Photography",
					Deliverables = new List<(string, string)>(),
					Duration = "1 hr",
					BasePrice = 0m,
					Description = description,
					Status = "Active"
				});
			}
		}
		catch
		{
			return GetMainServices();
		}
		return list;
	}

	public (byte[]? Image, string? MimeType) GetServiceImage(string code)
	{
		if (string.IsNullOrWhiteSpace(code))
		{
			return (Image: null, MimeType: null);
		}
		try
		{
			using SqlConnection sqlConnection = CreateConnection();
			sqlConnection.Open();
			using SqlCommand sqlCommand = new SqlCommand("\n                IF OBJECT_ID(N'dbo.sp_GetServiceDetailWithImage', N'P') IS NOT NULL\n                BEGIN\n                    EXEC dbo.sp_GetServiceDetailWithImage @ServicesID = @Id;\n                END\n                ELSE IF OBJECT_ID(N'dbo.Services', N'U') IS NOT NULL\n                BEGIN\n                    SELECT ServiceImage, ServiceImageMimeType\n                    FROM dbo.Services\n                    WHERE UPPER(ServicesID) = UPPER(@Id);\n                END", sqlConnection);
			sqlCommand.Parameters.AddWithValue("@Id", code.Trim());
			using SqlDataReader sqlDataReader = sqlCommand.ExecuteReader();
			if (sqlDataReader.Read())
			{
				if (sqlDataReader.FieldCount >= 5)
				{
					byte[] item = (sqlDataReader.IsDBNull(3) ? null : ((byte[])sqlDataReader[3]));
					string item2 = (sqlDataReader.IsDBNull(4) ? "image/jpeg" : sqlDataReader.GetString(4));
					return (Image: item, MimeType: item2);
				}
				byte[] item3 = (sqlDataReader.IsDBNull(0) ? null : ((byte[])sqlDataReader[0]));
				string item4 = (sqlDataReader.IsDBNull(1) ? "image/jpeg" : sqlDataReader.GetString(1));
				return (Image: item3, MimeType: item4);
			}
		}
		catch
		{
		}
		return (Image: null, MimeType: null);
	}

	public void UpdateServiceImage(string code, byte[] imageBytes, string mimeType, string? requestingAdminId = null)
	{
		ArgumentNullException.ThrowIfNull(imageBytes, "imageBytes");
		using SqlConnection sqlConnection = CreateConnection();
		sqlConnection.Open();
		using SqlCommand sqlCommand = new SqlCommand("\n            IF OBJECT_ID(N'dbo.sp_UpdateServiceImage', N'P') IS NOT NULL\n            BEGIN\n                EXEC dbo.sp_UpdateServiceImage @ServicesID = @Id, @ServiceImage = @Img, @ServiceImageMimeType = @Mime, @UpdatedBy = @By;\n            END\n            ELSE IF OBJECT_ID(N'dbo.Services', N'U') IS NOT NULL\n            BEGIN\n                UPDATE dbo.Services\n                SET ServiceImage = @Img,\n                    ServiceImageMimeType = @Mime\n                WHERE UPPER(ServicesID) = UPPER(@Id);\n            END", sqlConnection);
		sqlCommand.Parameters.AddWithValue("@Id", code.Trim());
		sqlCommand.Parameters.AddWithValue("@Img", imageBytes);
		sqlCommand.Parameters.AddWithValue("@Mime", string.IsNullOrWhiteSpace(mimeType) ? "image/jpeg" : mimeType);
		sqlCommand.Parameters.AddWithValue("@By", requestingAdminId ?? "Admin");
		sqlCommand.ExecuteNonQuery();
	}

	public MainServiceItem AddMainService(MainServiceModel model)
	{
		ArgumentNullException.ThrowIfNull(model, "model");
		using SqlConnection sqlConnection = CreateConnection();
		sqlConnection.Open();
		int count = GetMainServices().Count;
		string text = (string.IsNullOrWhiteSpace(model.Code) ? $"SRV-{count + 1:D3}" : model.Code.Trim());
		using SqlCommand sqlCommand = new SqlCommand("\n            INSERT INTO dbo.Services (ServicesID, ServiceName, ServicesDescription)\n            VALUES (@Id, @Name, @Desc);", sqlConnection);
		sqlCommand.Parameters.AddWithValue("@Id", text);
		sqlCommand.Parameters.AddWithValue("@Name", model.Name.Trim());
		sqlCommand.Parameters.AddWithValue("@Desc", string.IsNullOrWhiteSpace(model.Description) ? model.Category : model.Description.Trim());
		sqlCommand.ExecuteNonQuery();
		return new MainServiceItem
		{
			Code = text,
			Title = model.Name.Trim(),
			Category = (string.IsNullOrWhiteSpace(model.Category) ? "Studio" : model.Category),
			CategorySpec = (string.IsNullOrWhiteSpace(model.CategorySpec) ? "Photography" : model.CategorySpec),
			Deliverables = (model.Deliverables ?? new List<(string, string)>()),
			Duration = $"{model.DurationHours:0.#} hrs",
			BasePrice = model.BasePrice,
			Description = model.Description,
			Status = model.Status
		};
	}

	public IReadOnlyList<PackageServiceItem> GetPackageServices()
	{
		List<PackageServiceItem> list = new List<PackageServiceItem>();
		using SqlConnection sqlConnection = CreateConnection();
		sqlConnection.Open();
		using SqlCommand sqlCommand = new SqlCommand("\n            IF OBJECT_ID(N'dbo.ComboPackage', N'U') IS NOT NULL\n            BEGIN\n                SELECT ComboPackageID, ComboPackageName, ComboPackageDescription\n                FROM dbo.ComboPackage\n                ORDER BY ComboPackageID;\n            END", sqlConnection);
		using SqlDataReader sqlDataReader = sqlCommand.ExecuteReader();
		while (sqlDataReader.Read())
		{
			list.Add(new PackageServiceItem
			{
				Code = sqlDataReader.GetString(0),
				Title = sqlDataReader.GetString(1),
				BundleCategory = "Package",
				Inclusions = new List<(string, string)>(),
				Price = "$0",
				Description = (sqlDataReader.IsDBNull(2) ? null : sqlDataReader.GetString(2)),
				Status = "Active"
			});
		}
		return list;
	}

	public IReadOnlyList<PackageServiceItem> GetPackageServicesSummary()
	{
		List<PackageServiceItem> list = new List<PackageServiceItem>();
		try
		{
			using SqlConnection sqlConnection = CreateConnection();
			sqlConnection.Open();
			using SqlCommand sqlCommand = new SqlCommand("\n                IF OBJECT_ID(N'dbo.sp_GetComboPackagesSummary', N'P') IS NOT NULL\n                BEGIN\n                    EXEC dbo.sp_GetComboPackagesSummary;\n                END\n                ELSE IF OBJECT_ID(N'dbo.ComboPackage', N'U') IS NOT NULL\n                BEGIN\n                    SELECT ComboPackageID, ComboPackageName, ComboPackageDescription\n                    FROM dbo.ComboPackage\n                    ORDER BY ComboPackageID;\n                END", sqlConnection);
			using SqlDataReader sqlDataReader = sqlCommand.ExecuteReader();
			while (sqlDataReader.Read())
			{
				list.Add(new PackageServiceItem
				{
					Code = sqlDataReader.GetString(0),
					Title = sqlDataReader.GetString(1),
					BundleCategory = "Package",
					Inclusions = new List<(string, string)>(),
					Price = "$0",
					Description = (sqlDataReader.IsDBNull(2) ? null : sqlDataReader.GetString(2)),
					Status = "Active"
				});
			}
		}
		catch
		{
			return GetPackageServices();
		}
		return list;
	}

	public (byte[]? Image, string? MimeType) GetComboPackageImage(string code)
	{
		if (string.IsNullOrWhiteSpace(code))
		{
			return (Image: null, MimeType: null);
		}
		try
		{
			using SqlConnection sqlConnection = CreateConnection();
			sqlConnection.Open();
			using SqlCommand sqlCommand = new SqlCommand("\n                IF OBJECT_ID(N'dbo.sp_GetComboPackageDetailWithImage', N'P') IS NOT NULL\n                BEGIN\n                    EXEC dbo.sp_GetComboPackageDetailWithImage @ComboPackageID = @Id;\n                END\n                ELSE IF OBJECT_ID(N'dbo.ComboPackage', N'U') IS NOT NULL\n                BEGIN\n                    SELECT ComboPackageImage, ComboPackageImageMimeType\n                    FROM dbo.ComboPackage\n                    WHERE UPPER(ComboPackageID) = UPPER(@Id);\n                END", sqlConnection);
			sqlCommand.Parameters.AddWithValue("@Id", code.Trim());
			using SqlDataReader sqlDataReader = sqlCommand.ExecuteReader();
			if (sqlDataReader.Read())
			{
				if (sqlDataReader.FieldCount >= 5)
				{
					byte[] item = (sqlDataReader.IsDBNull(3) ? null : ((byte[])sqlDataReader[3]));
					string item2 = (sqlDataReader.IsDBNull(4) ? "image/jpeg" : sqlDataReader.GetString(4));
					return (Image: item, MimeType: item2);
				}
				byte[] item3 = (sqlDataReader.IsDBNull(0) ? null : ((byte[])sqlDataReader[0]));
				string item4 = (sqlDataReader.IsDBNull(1) ? "image/jpeg" : sqlDataReader.GetString(1));
				return (Image: item3, MimeType: item4);
			}
		}
		catch
		{
		}
		return (Image: null, MimeType: null);
	}

	public void UpdateComboPackageImage(string code, byte[] imageBytes, string mimeType, string? requestingAdminId = null)
	{
		ArgumentNullException.ThrowIfNull(imageBytes, "imageBytes");
		using SqlConnection sqlConnection = CreateConnection();
		sqlConnection.Open();
		using SqlCommand sqlCommand = new SqlCommand("\n            IF OBJECT_ID(N'dbo.sp_UpdateComboPackageImage', N'P') IS NOT NULL\n            BEGIN\n                EXEC dbo.sp_UpdateComboPackageImage @ComboPackageID = @Id, @ComboPackageImage = @Img, @ComboPackageImageMimeType = @Mime, @UpdatedBy = @By;\n            END\n            ELSE IF OBJECT_ID(N'dbo.ComboPackage', N'U') IS NOT NULL\n            BEGIN\n                UPDATE dbo.ComboPackage\n                SET ComboPackageImage = @Img,\n                    ComboPackageImageMimeType = @Mime\n                WHERE UPPER(ComboPackageID) = UPPER(@Id);\n            END", sqlConnection);
		sqlCommand.Parameters.AddWithValue("@Id", code.Trim());
		sqlCommand.Parameters.AddWithValue("@Img", imageBytes);
		sqlCommand.Parameters.AddWithValue("@Mime", string.IsNullOrWhiteSpace(mimeType) ? "image/jpeg" : mimeType);
		sqlCommand.Parameters.AddWithValue("@By", requestingAdminId ?? "Admin");
		sqlCommand.ExecuteNonQuery();
	}

	public PackageServiceItem AddPackageService(PackageServiceModel model)
	{
		ArgumentNullException.ThrowIfNull(model, "model");
		using SqlConnection sqlConnection = CreateConnection();
		sqlConnection.Open();
		int count = GetPackageServices().Count;
		string text = (string.IsNullOrWhiteSpace(model.Code) ? $"PKG-{count + 1:D3}" : model.Code.Trim());
		using SqlCommand sqlCommand = new SqlCommand("\n            INSERT INTO dbo.ComboPackage (ComboPackageID, ComboPackageName, ComboPackageDescription)\n            VALUES (@Id, @Name, @Desc);", sqlConnection);
		sqlCommand.Parameters.AddWithValue("@Id", text);
		sqlCommand.Parameters.AddWithValue("@Name", model.Name.Trim());
		sqlCommand.Parameters.AddWithValue("@Desc", string.IsNullOrWhiteSpace(model.Description) ? model.TargetCategory : model.Description.Trim());
		sqlCommand.ExecuteNonQuery();
		return new PackageServiceItem
		{
			Code = text,
			Title = model.Name.Trim(),
			BundleCategory = (string.IsNullOrWhiteSpace(model.TargetCategory) ? "Package" : model.TargetCategory),
			Inclusions = (model.Inclusions ?? new List<(string, string)>()),
			Price = $"${model.Price:N0}",
			SavingsNote = model.SavingsNote,
			Description = model.Description,
			Status = model.Status
		};
	}

	public async Task<string> GetNextComboPackageIdAsync(CancellationToken ct = default(CancellationToken))
	{
		if (_httpClientFactory != null)
		{
			try
			{
				HttpClient client = _httpClientFactory.CreateClient("AevumApi.Authorized");
				HttpResponseMessage response = await client.GetAsync("api/packages/next-id", ct);
				if (response.IsSuccessStatusCode)
				{
					JsonElement result = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
					if (result.TryGetProperty("packageId", out var pkgId) || result.TryGetProperty("nextId", out pkgId))
					{
						string val = pkgId.GetString();
						if (!string.IsNullOrWhiteSpace(val))
						{
							return val;
						}
					}
				}
			}
			catch
			{
			}
		}
		try
		{
			await using SqlConnection conn = CreateConnection();
			await conn.OpenAsync(ct);
			string result2;
			await using (SqlCommand cmd = new SqlCommand("dbo.sp_GetNextComboPackageId", conn)
			{
				CommandType = CommandType.StoredProcedure
			})
			{
				SqlParameter outParam = new SqlParameter("@NextPackageID", SqlDbType.NVarChar, 30)
				{
					Direction = ParameterDirection.Output
				};
				cmd.Parameters.Add(outParam);
				await cmd.ExecuteNonQueryAsync(ct);
				string nextId = outParam.Value?.ToString();
				if (!string.IsNullOrWhiteSpace(nextId))
				{
					result2 = nextId;
					goto IL_04fb;
				}
			}
			goto end_IL_02b2;
			IL_04fb:
			return result2;
			end_IL_02b2:;
		}
		catch
		{
		}
		try
		{
			string result3;
			await using (SqlConnection conn2 = CreateConnection())
			{
				await conn2.OpenAsync(ct);
				string text;
				await using (SqlCommand cmd2 = new SqlCommand("\n                IF EXISTS (SELECT 1 FROM sys.sequences WHERE name = 'Seq_ComboPackageID')\n                BEGIN\n                    DECLARE @CurVal SQL_VARIANT;\n                    SELECT @CurVal = current_value FROM sys.sequences WHERE name = 'Seq_ComboPackageID';\n                    DECLARE @NextNum INT = 1;\n                    IF @CurVal IS NOT NULL\n                    BEGIN\n                        DECLARE @ExistingCount INT = 0;\n                        SELECT @ExistingCount = COUNT(*) FROM dbo.ComboPackage WHERE ComboPackageID LIKE 'PKG-%';\n                        IF @ExistingCount > 0 OR CAST(@CurVal AS INT) > 1\n                            SET @NextNum = CAST(@CurVal AS INT) + 1;\n                    END\n                    SELECT 'PKG-' + RIGHT('0000' + CAST(@NextNum AS VARCHAR(10)), 4);\n                END\n                ELSE\n                BEGIN\n                    DECLARE @MaxVal INT = 0;\n                    IF OBJECT_ID(N'dbo.ComboPackage', N'U') IS NOT NULL\n                    BEGIN\n                        SELECT @MaxVal = ISNULL(MAX(TRY_CAST(SUBSTRING(ComboPackageID, 5, 20) AS INT)), 0)\n                        FROM dbo.ComboPackage WITH (NOLOCK)\n                        WHERE ComboPackageID LIKE 'PKG-%';\n                    END\n                    SELECT 'PKG-' + RIGHT('0000' + CAST(@MaxVal + 1 AS VARCHAR(10)), 4);\n                END", conn2))
				{
					text = (await cmd2.ExecuteScalarAsync(ct))?.ToString() ?? "PKG-0001";
				}
				result3 = text;
			}
			return result3;
		}
		catch
		{
			int fallbackCount = GetPackageServices().Count + 1;
			return $"PKG-{fallbackCount:D4}";
		}
	}

	public async Task<IReadOnlyList<CategoryLookupModel>> GetServiceCategoriesLookupAsync(CancellationToken ct = default(CancellationToken))
	{
		if (_httpClientFactory != null)
		{
			try
			{
				HttpClient client = _httpClientFactory.CreateClient("AevumApi.Authorized");
				HttpResponseMessage response = await client.GetAsync("api/packages/categories", ct);
				if (response.IsSuccessStatusCode)
				{
					List<CategoryLookupModel> apiList = await response.Content.ReadFromJsonAsync<List<CategoryLookupModel>>(ct);
					if (apiList != null && apiList.Count > 0)
					{
						return apiList;
					}
				}
			}
			catch
			{
			}
		}
		List<CategoryLookupModel> list = new List<CategoryLookupModel>();
		try
		{
			await using SqlConnection conn = CreateConnection();
			await conn.OpenAsync(ct);
			await using SqlCommand cmd = new SqlCommand("\n                IF OBJECT_ID(N'dbo.ServiceCategory', N'U') IS NOT NULL\n                BEGIN\n                    SELECT ServiceCategoryID, ServiceCategoryName, ServiceCategoryDescription\n                    FROM dbo.ServiceCategory\n                    ORDER BY ServiceCategoryName;\n                END", conn);
			await using SqlDataReader reader = await cmd.ExecuteReaderAsync(ct);
			while (await reader.ReadAsync(ct))
			{
				list.Add(new CategoryLookupModel
				{
					Id = reader.GetString(0),
					Name = reader.GetString(1),
					Description = (reader.IsDBNull(2) ? null : reader.GetString(2))
				});
			}
		}
		catch
		{
		}
		if (list.Count == 0)
		{
			list.AddRange(new CategoryLookupModel[4]
			{
				new CategoryLookupModel
				{
					Id = "CAT-PORT",
					Name = "Portrait Photography",
					Description = "Studio headshots, family portraits, and graduation"
				},
				new CategoryLookupModel
				{
					Id = "CAT-WEDD",
					Name = "Wedding & Reception",
					Description = "Pre-nuptial, ceremony, and all-day wedding coverage"
				},
				new CategoryLookupModel
				{
					Id = "CAT-COMM",
					Name = "Commercial & Editorial",
					Description = "Corporate branding, product showcases, and fashion"
				},
				new CategoryLookupModel
				{
					Id = "CAT-EVNT",
					Name = "Event Photography",
					Description = "Birthdays, corporate events, and live celebrations"
				}
			});
		}
		return list;
	}

	public async Task<IReadOnlyList<BundleServiceOption>> GetAvailableServicesForBundlingAsync(CancellationToken ct = default(CancellationToken))
	{
		if (_httpClientFactory != null)
		{
			try
			{
				HttpClient client = _httpClientFactory.CreateClient("AevumApi.Authorized");
				HttpResponseMessage response = await client.GetAsync("api/packages/available-services", ct);
				if (response.IsSuccessStatusCode)
				{
					List<BundleServiceOption> apiList = await response.Content.ReadFromJsonAsync<List<BundleServiceOption>>(ct);
					if (apiList != null && apiList.Count > 0)
					{
						return apiList;
					}
				}
			}
			catch
			{
			}
		}
		List<BundleServiceOption> list = new List<BundleServiceOption>();
		try
		{
			await using SqlConnection conn = CreateConnection();
			await conn.OpenAsync(ct);
			await using SqlCommand cmd = new SqlCommand("\n                IF OBJECT_ID(N'dbo.sp_GetAvailableServicesForBundling', N'P') IS NOT NULL\n                BEGIN\n                    EXEC dbo.sp_GetAvailableServicesForBundling;\n                END\n                ELSE\n                BEGIN\n                    IF OBJECT_ID(N'dbo.Services', N'U') IS NOT NULL\n                    BEGIN\n                        SELECT ServicesID, ServiceName, ServicesDescription, ServicePrice, 'Main' AS ServiceType,\n                               CAST(CASE WHEN ServiceImage IS NOT NULL THEN 1 ELSE 0 END AS bit) AS HasImage\n                        FROM dbo.Services\n                    END\n                    UNION ALL\n                    IF OBJECT_ID(N'dbo.AdditionalServices', N'U') IS NOT NULL\n                    BEGIN\n                        SELECT AdditionalServicesID, AdditionalServiceName, AdditionalServicesDescription, 0.00 AS ServicePrice, 'Additional' AS ServiceType,\n                               CAST(CASE WHEN AdditionalServiceImage IS NOT NULL THEN 1 ELSE 0 END AS bit) AS HasImage\n                        FROM dbo.AdditionalServices\n                    END\n                    ORDER BY ServiceType DESC, ServiceName ASC;\n                END", conn);
			await using SqlDataReader reader = await cmd.ExecuteReaderAsync(ct);
			while (await reader.ReadAsync(ct))
			{
				list.Add(new BundleServiceOption
				{
					Id = reader.GetString(0),
					Name = reader.GetString(1),
					Description = (reader.IsDBNull(2) ? string.Empty : reader.GetString(2)),
					Price = (reader.IsDBNull(3) ? 0m : reader.GetDecimal(3)),
					ServiceType = reader.GetString(4),
					HasImage = (!reader.IsDBNull(5) && reader.GetBoolean(5))
				});
			}
		}
		catch
		{
		}
		if (list.Count == 0)
		{
			foreach (MainServiceItem m in GetMainServices())
			{
				list.Add(new BundleServiceOption
				{
					Id = m.Code,
					Name = m.Title,
					Description = m.Description,
					Price = m.BasePrice,
					ServiceType = "Main",
					HasImage = false
				});
			}
			foreach (AddOnServiceItem a in GetAdditionalServices())
			{
				list.Add(new BundleServiceOption
				{
					Id = a.Code,
					Name = a.Title,
					Description = a.Description,
					Price = 50.00m,
					ServiceType = "Additional",
					HasImage = false
				});
			}
		}
		return list;
	}

	public async Task<PackageServiceItem> CreateComboPackageViaApiAsync(PackageWizardFormModel formModel, CancellationToken ct = default(CancellationToken))
	{
		ArgumentNullException.ThrowIfNull(formModel, "formModel");
		if (_httpClientFactory != null)
		{
			try
			{
				HttpClient client = _httpClientFactory.CreateClient("AevumApi.Authorized");
				using MultipartFormDataContent form = new MultipartFormDataContent();
				form.Add(new StringContent(formModel.Name.Trim()), "Name");
				form.Add(new StringContent(formModel.Description?.Trim() ?? string.Empty), "Description");
				form.Add(new StringContent(formModel.CategoryId.Trim()), "CategoryId");
				form.Add(new StringContent(formModel.Price.ToString(CultureInfo.InvariantCulture)), "Price");
				if (formModel.SelectedServiceIds != null)
				{
					foreach (string sId in formModel.SelectedServiceIds)
					{
						form.Add(new StringContent(sId), "SelectedServiceIds");
					}
					string json = JsonSerializer.Serialize(formModel.SelectedServiceIds);
					form.Add(new StringContent(json), "SelectedServiceIdsRaw");
				}
				if (formModel.ImageBytes != null && formModel.ImageBytes.Length != 0)
				{
					ByteArrayContent fileContent = new ByteArrayContent(formModel.ImageBytes);
					string mime = (string.IsNullOrWhiteSpace(formModel.ImageMimeType) ? "image/jpeg" : formModel.ImageMimeType);
					fileContent.Headers.ContentType = new MediaTypeHeaderValue(mime);
					form.Add(fileContent, "ImageFile", formModel.ImageFileName ?? "package-hero.jpg");
				}
				HttpResponseMessage response = await client.PostAsync("api/packages", form, ct);
				if (response.IsSuccessStatusCode)
				{
					JsonElement created = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
					string packageId = (created.TryGetProperty("packageId", out var pid) ? (pid.GetString() ?? formModel.GeneratedPackageId) : formModel.GeneratedPackageId);
					string title = (created.TryGetProperty("name", out var ptitle) ? (ptitle.GetString() ?? formModel.Name) : formModel.Name);
					return new PackageServiceItem
					{
						Code = packageId,
						Title = title,
						BundleCategory = formModel.CategoryName,
						Description = formModel.Description,
						Price = $"${formModel.Price:N0}",
						Status = "Active"
					};
				}
				string errBody = await response.Content.ReadAsStringAsync(ct);
				if (response.StatusCode == HttpStatusCode.BadRequest)
				{
					throw new InvalidOperationException("API Validation Failed: " + errBody);
				}
			}
			catch (InvalidOperationException)
			{
				throw;
			}
			catch
			{
			}
		}
		string text = ((!string.IsNullOrWhiteSpace(formModel.GeneratedPackageId)) ? formModel.GeneratedPackageId : (await GetNextComboPackageIdAsync(ct)));
		string packageCode = text;
		PackageServiceItem result;
		await using (SqlConnection conn = CreateConnection())
		{
			await conn.OpenAsync(ct);
			await using (SqlCommand prepCmd = new SqlCommand("\n            IF OBJECT_ID(N'dbo.ComboPackage', N'U') IS NOT NULL\n            BEGIN\n                IF COL_LENGTH(N'dbo.ComboPackage', N'ComboPackagePrice') IS NULL\n                    ALTER TABLE dbo.ComboPackage ADD ComboPackagePrice decimal(18,2) NOT NULL CONSTRAINT DF_ComboPackage_Price DEFAULT 0.00;\n            END;\n            IF OBJECT_ID(N'dbo.ComboPackageJoin', N'U') IS NOT NULL\n            BEGIN\n                ALTER TABLE dbo.ComboPackageJoin ALTER COLUMN ServicesID nvarchar(30) NULL;\n            END;", conn))
			{
				await prepCmd.ExecuteNonQueryAsync(ct);
			}
			PackageServiceItem packageServiceItem;
			await using (SqlTransaction trans = (SqlTransaction)(await conn.BeginTransactionAsync(IsolationLevel.Serializable, ct)))
			{
				try
				{
					await using (SqlCommand lockCmd = new SqlCommand("\n                DECLARE @res INT;\n                EXEC @res = sp_getapplock @Resource = 'ComboPackage_IdGen_Lock', @LockMode = 'Exclusive', @LockOwner = 'Transaction';\n                SELECT @res;", conn, trans))
					{
						await lockCmd.ExecuteScalarAsync(ct);
					}
					await using (SqlCommand cmd = new SqlCommand("\n                INSERT INTO dbo.ComboPackage (\n                    ComboPackageID, ComboPackageName, ComboPackageDescription, ComboPackagePrice, ComboPackageImage, ComboPackageImageMimeType\n                )\n                VALUES (@Id, @Name, @Desc, @Price, @Img, @Mime);", conn, trans))
					{
						cmd.Parameters.AddWithValue("@Id", packageCode);
						cmd.Parameters.AddWithValue("@Name", formModel.Name.Trim());
						cmd.Parameters.AddWithValue("@Desc", formModel.Description?.Trim() ?? string.Empty);
						cmd.Parameters.AddWithValue("@Price", formModel.Price);
						cmd.Parameters.Add("@Img", SqlDbType.VarBinary, -1).Value = ((object)formModel.ImageBytes) ?? ((object)DBNull.Value);
						cmd.Parameters.AddWithValue("@Mime", string.IsNullOrWhiteSpace(formModel.ImageMimeType) ? "image/jpeg" : formModel.ImageMimeType.Trim());
						await cmd.ExecuteNonQueryAsync(ct);
					}
					if (!string.IsNullOrWhiteSpace(formModel.CategoryId))
					{
						await using SqlCommand catCmd = new SqlCommand("\n                    IF NOT EXISTS (SELECT 1 FROM dbo.ServiceCategory WHERE ServiceCategoryID = @CatId)\n                    BEGIN\n                        INSERT INTO dbo.ServiceCategory (ServiceCategoryID, ServiceCategoryName, ServiceCategoryDescription)\n                        VALUES (@CatId, @CatId, @CatId);\n                    END;\n                    INSERT INTO dbo.ServiceCategoryJoins (ServiceCategoryJoinID, ServiceCategoryID, ServicesID, AdditionalServicesID, ComboPackageID)\n                    VALUES ('SCJ-' + SUBSTRING(REPLACE(CAST(NEWID() AS nvarchar(36)), '-', ''), 1, 10), @CatId, NULL, NULL, @PkgId);", conn, trans);
						catCmd.Parameters.AddWithValue("@CatId", formModel.CategoryId.Trim());
						catCmd.Parameters.AddWithValue("@PkgId", packageCode);
						await catCmd.ExecuteNonQueryAsync(ct);
					}
					if (formModel.SelectedServiceIds != null && formModel.SelectedServiceIds.Count > 0)
					{
						foreach (string svcId in formModel.SelectedServiceIds)
						{
							if (!string.IsNullOrWhiteSpace(svcId))
							{
								await using SqlCommand joinCmd = new SqlCommand("\n                        DECLARE @JID nvarchar(30) = 'CPJ-' + SUBSTRING(REPLACE(CAST(NEWID() AS nvarchar(36)), '-', ''), 1, 10);\n                        IF EXISTS (SELECT 1 FROM dbo.Services WHERE ServicesID = @SvcId)\n                        BEGIN\n                            INSERT INTO dbo.ComboPackageJoin (ComboPackageJoinID, ComboPackageID, ServicesID, AdditionalServicesID)\n                            VALUES (@JID, @PkgId, @SvcId, NULL);\n                        END\n                        ELSE IF EXISTS (SELECT 1 FROM dbo.AdditionalServices WHERE AdditionalServicesID = @SvcId)\n                        BEGIN\n                            INSERT INTO dbo.ComboPackageJoin (ComboPackageJoinID, ComboPackageID, ServicesID, AdditionalServicesID)\n                            VALUES (@JID, @PkgId, NULL, @SvcId);\n                        END\n                        ELSE\n                        BEGIN\n                            INSERT INTO dbo.ComboPackageJoin (ComboPackageJoinID, ComboPackageID, ServicesID, AdditionalServicesID)\n                            VALUES (@JID, @PkgId, @SvcId, NULL);\n                        END;", conn, trans);
								joinCmd.Parameters.AddWithValue("@PkgId", packageCode);
								joinCmd.Parameters.AddWithValue("@SvcId", svcId.Trim());
								await joinCmd.ExecuteNonQueryAsync(ct);
							}
						}
					}
					await trans.CommitAsync(ct);
					packageServiceItem = new PackageServiceItem
					{
						Code = packageCode,
						Title = formModel.Name.Trim(),
						BundleCategory = formModel.CategoryName,
						Description = formModel.Description,
						Price = $"${formModel.Price:N0}",
						Status = "Active"
					};
				}
				catch
				{
					await trans.RollbackAsync(ct);
					throw;
				}
			}
			result = packageServiceItem;
		}
		return result;
	}

	public IReadOnlyList<AddOnServiceItem> GetAdditionalServices()
	{
		List<AddOnServiceItem> list = new List<AddOnServiceItem>();
		using SqlConnection sqlConnection = CreateConnection();
		sqlConnection.Open();
		using SqlCommand sqlCommand = new SqlCommand("\n            IF OBJECT_ID(N'dbo.AdditionalServices', N'U') IS NOT NULL\n            BEGIN\n                SELECT AdditionalServicesID, AdditionalServiceName, AdditionalServicesDescription\n                FROM dbo.AdditionalServices\n                ORDER BY AdditionalServicesID;\n            END", sqlConnection);
		using SqlDataReader sqlDataReader = sqlCommand.ExecuteReader();
		while (sqlDataReader.Read())
		{
			list.Add(new AddOnServiceItem
			{
				Code = sqlDataReader.GetString(0),
				Title = sqlDataReader.GetString(1),
				TypeCategory = "Add-On",
				FilterCategory = "Add-On",
				PriceText = "$0",
				CompatibleServices = new List<(string, string)>(),
				Description = (sqlDataReader.IsDBNull(2) ? null : sqlDataReader.GetString(2)),
				Status = "Active"
			});
		}
		return list;
	}

	public IReadOnlyList<AddOnServiceItem> GetAdditionalServicesSummary()
	{
		List<AddOnServiceItem> list = new List<AddOnServiceItem>();
		try
		{
			using SqlConnection sqlConnection = CreateConnection();
			sqlConnection.Open();
			using SqlCommand sqlCommand = new SqlCommand("\n                IF OBJECT_ID(N'dbo.sp_GetAdditionalServicesSummary', N'P') IS NOT NULL\n                BEGIN\n                    EXEC dbo.sp_GetAdditionalServicesSummary;\n                END\n                ELSE IF OBJECT_ID(N'dbo.AdditionalServices', N'U') IS NOT NULL\n                BEGIN\n                    SELECT AdditionalServicesID, AdditionalServiceName, AdditionalServicesDescription\n                    FROM dbo.AdditionalServices\n                    ORDER BY AdditionalServicesID;\n                END", sqlConnection);
			using SqlDataReader sqlDataReader = sqlCommand.ExecuteReader();
			while (sqlDataReader.Read())
			{
				list.Add(new AddOnServiceItem
				{
					Code = sqlDataReader.GetString(0),
					Title = sqlDataReader.GetString(1),
					TypeCategory = "Add-On",
					FilterCategory = "Add-On",
					PriceText = "$0",
					CompatibleServices = new List<(string, string)>(),
					Description = (sqlDataReader.IsDBNull(2) ? null : sqlDataReader.GetString(2)),
					Status = "Active"
				});
			}
		}
		catch
		{
			return GetAdditionalServices();
		}
		return list;
	}

	public (byte[]? Image, string? MimeType) GetAdditionalServiceImage(string code)
	{
		if (string.IsNullOrWhiteSpace(code))
		{
			return (Image: null, MimeType: null);
		}
		try
		{
			using SqlConnection sqlConnection = CreateConnection();
			sqlConnection.Open();
			using SqlCommand sqlCommand = new SqlCommand("\n                IF OBJECT_ID(N'dbo.sp_GetAdditionalServiceDetailWithImage', N'P') IS NOT NULL\n                BEGIN\n                    EXEC dbo.sp_GetAdditionalServiceDetailWithImage @AdditionalServicesID = @Id;\n                END\n                ELSE IF OBJECT_ID(N'dbo.AdditionalServices', N'U') IS NOT NULL\n                BEGIN\n                    SELECT AdditionalServiceImage, AdditionalServiceImageMimeType\n                    FROM dbo.AdditionalServices\n                    WHERE UPPER(AdditionalServicesID) = UPPER(@Id);\n                END", sqlConnection);
			sqlCommand.Parameters.AddWithValue("@Id", code.Trim());
			using SqlDataReader sqlDataReader = sqlCommand.ExecuteReader();
			if (sqlDataReader.Read())
			{
				if (sqlDataReader.FieldCount >= 5)
				{
					byte[] item = (sqlDataReader.IsDBNull(3) ? null : ((byte[])sqlDataReader[3]));
					string item2 = (sqlDataReader.IsDBNull(4) ? "image/jpeg" : sqlDataReader.GetString(4));
					return (Image: item, MimeType: item2);
				}
				byte[] item3 = (sqlDataReader.IsDBNull(0) ? null : ((byte[])sqlDataReader[0]));
				string item4 = (sqlDataReader.IsDBNull(1) ? "image/jpeg" : sqlDataReader.GetString(1));
				return (Image: item3, MimeType: item4);
			}
		}
		catch
		{
		}
		return (Image: null, MimeType: null);
	}

	public void UpdateAdditionalServiceImage(string code, byte[] imageBytes, string mimeType, string? requestingAdminId = null)
	{
		ArgumentNullException.ThrowIfNull(imageBytes, "imageBytes");
		using SqlConnection sqlConnection = CreateConnection();
		sqlConnection.Open();
		using SqlCommand sqlCommand = new SqlCommand("\n            IF OBJECT_ID(N'dbo.sp_UpdateAdditionalServiceImage', N'P') IS NOT NULL\n            BEGIN\n                EXEC dbo.sp_UpdateAdditionalServiceImage @AdditionalServicesID = @Id, @AdditionalServiceImage = @Img, @AdditionalServiceImageMimeType = @Mime, @UpdatedBy = @By;\n            END\n            ELSE IF OBJECT_ID(N'dbo.AdditionalServices', N'U') IS NOT NULL\n            BEGIN\n                UPDATE dbo.AdditionalServices\n                SET AdditionalServiceImage = @Img,\n                    AdditionalServiceImageMimeType = @Mime\n                WHERE UPPER(AdditionalServicesID) = UPPER(@Id);\n            END", sqlConnection);
		sqlCommand.Parameters.AddWithValue("@Id", code.Trim());
		sqlCommand.Parameters.AddWithValue("@Img", imageBytes);
		sqlCommand.Parameters.AddWithValue("@Mime", string.IsNullOrWhiteSpace(mimeType) ? "image/jpeg" : mimeType);
		sqlCommand.Parameters.AddWithValue("@By", requestingAdminId ?? "Admin");
		sqlCommand.ExecuteNonQuery();
	}

	public AddOnServiceItem AddAdditionalService(AddOnServiceModel model)
	{
		ArgumentNullException.ThrowIfNull(model, "model");
		using SqlConnection sqlConnection = CreateConnection();
		sqlConnection.Open();
		int count = GetAdditionalServices().Count;
		string text = (string.IsNullOrWhiteSpace(model.Code) ? $"ADD-{count + 1:D3}" : model.Code.Trim());
		using SqlCommand sqlCommand = new SqlCommand("\n            INSERT INTO dbo.AdditionalServices (AdditionalServicesID, AdditionalServiceName, AdditionalServicesDescription)\n            VALUES (@Id, @Name, @Desc);", sqlConnection);
		sqlCommand.Parameters.AddWithValue("@Id", text);
		sqlCommand.Parameters.AddWithValue("@Name", model.Title.Trim());
		sqlCommand.Parameters.AddWithValue("@Desc", string.IsNullOrWhiteSpace(model.Description) ? model.TypeCategory : model.Description.Trim());
		sqlCommand.ExecuteNonQuery();
		return new AddOnServiceItem
		{
			Code = text,
			Title = model.Title.Trim(),
			TypeCategory = (string.IsNullOrWhiteSpace(model.TypeCategory) ? "Add-On" : model.TypeCategory),
			FilterCategory = (string.IsNullOrWhiteSpace(model.FilterCategory) ? "Add-On" : model.FilterCategory),
			PriceText = $"${model.Rate:N0} {model.RateUnit}",
			CompatibleServices = (model.CompatibleServices ?? new List<(string, string)>()),
			Description = model.Description,
			Status = model.Status
		};
	}

	public List<EquipmentRowDto> GetEquipmentRows()
	{
		List<EquipmentRowDto> list = new List<EquipmentRowDto>();
		using SqlConnection sqlConnection = CreateConnection();
		sqlConnection.Open();
		using SqlCommand sqlCommand = new SqlCommand("\n            IF OBJECT_ID(N'dbo.Equipment', N'U') IS NOT NULL\n            BEGIN\n                SELECT eq.EquipmentID,\n                       eq.EquipmentName,\n                       eq.EquipmentTypeID,\n                       COALESCE(et.EquipmentTypeName, eq.EquipmentTypeID) AS TypeName,\n                       COALESCE(eq.SerialNo, eq.EquipmentID) AS SerialNo,\n                       eq.EquipmentStatus,\n                       eq.EquipmentImage\n                FROM dbo.Equipment eq\n                LEFT JOIN dbo.EquipmentType et ON et.EquipmentTypeID = eq.EquipmentTypeID\n                ORDER BY eq.EquipmentID;\n            END", sqlConnection);
		using SqlDataReader sqlDataReader = sqlCommand.ExecuteReader();
		while (sqlDataReader.Read())
		{
			list.Add(new EquipmentRowDto(sqlDataReader.GetString(0), sqlDataReader.GetString(1), sqlDataReader.GetString(2), sqlDataReader.IsDBNull(3) ? "General" : sqlDataReader.GetString(3), sqlDataReader.IsDBNull(4) ? string.Empty : sqlDataReader.GetString(4), sqlDataReader.IsDBNull(5) ? "Available" : sqlDataReader.GetString(5), sqlDataReader.IsDBNull(6) ? null : sqlDataReader.GetString(6)));
		}
		return list;
	}

	public List<EquipmentTypeRowDto> GetEquipmentTypes()
	{
		List<EquipmentTypeRowDto> list = new List<EquipmentTypeRowDto>();
		using SqlConnection sqlConnection = CreateConnection();
		sqlConnection.Open();
		using SqlCommand sqlCommand = new SqlCommand("\n            IF OBJECT_ID(N'dbo.EquipmentType', N'U') IS NOT NULL\n            BEGIN\n                SELECT et.EquipmentTypeID,\n                       et.EquipmentTypeName,\n                       COUNT(eq.EquipmentID) AS TotalUnits,\n                       SUM(CASE WHEN eq.EquipmentStatus = 'Available' THEN 1 ELSE 0 END) AS AvailableUnits\n                FROM dbo.EquipmentType et\n                LEFT JOIN dbo.Equipment eq ON eq.EquipmentTypeID = et.EquipmentTypeID\n                GROUP BY et.EquipmentTypeID, et.EquipmentTypeName\n                ORDER BY et.EquipmentTypeID;\n            END", sqlConnection);
		using SqlDataReader sqlDataReader = sqlCommand.ExecuteReader();
		while (sqlDataReader.Read())
		{
			list.Add(new EquipmentTypeRowDto(sqlDataReader.GetString(0), sqlDataReader.GetString(1), (!sqlDataReader.IsDBNull(2)) ? sqlDataReader.GetInt32(2) : 0, (!sqlDataReader.IsDBNull(3)) ? sqlDataReader.GetInt32(3) : 0));
		}
		return list;
	}

	public EquipmentRowDto AddEquipment(string name, string typeName, string serialNo, string status = "Available")
	{
		using SqlConnection sqlConnection = CreateConnection();
		sqlConnection.Open();
		string text = (string.IsNullOrWhiteSpace(typeName) ? "Camera" : typeName.Trim());
		string text2 = "ET-" + text.ToUpperInvariant().Substring(0, Math.Min(3, text.Length));
		using (SqlCommand sqlCommand = new SqlCommand("\n            IF NOT EXISTS (SELECT 1 FROM dbo.EquipmentType WHERE EquipmentTypeID = @TypeId)\n            BEGIN\n                INSERT INTO dbo.EquipmentType (EquipmentTypeID, EquipmentTypeName)\n                VALUES (@TypeId, @TypeName);\n            END", sqlConnection))
		{
			sqlCommand.Parameters.AddWithValue("@TypeId", text2);
			sqlCommand.Parameters.AddWithValue("@TypeName", text);
			sqlCommand.ExecuteNonQuery();
		}
		int count = GetEquipmentRows().Count;
		string text3 = $"EQ-{count + 1:D3}";
		string text4 = (string.IsNullOrWhiteSpace(serialNo) ? text3 : serialNo.Trim());
		using (SqlCommand sqlCommand2 = new SqlCommand("\n            INSERT INTO dbo.Equipment (EquipmentID, EquipmentName, EquipmentTypeID, EquipmentStatus, SerialNo)\n            VALUES (@Id, @Name, @TypeId, @Status, @SerialNo);", sqlConnection))
		{
			sqlCommand2.Parameters.AddWithValue("@Id", text3);
			sqlCommand2.Parameters.AddWithValue("@Name", name.Trim());
			sqlCommand2.Parameters.AddWithValue("@TypeId", text2);
			sqlCommand2.Parameters.AddWithValue("@Status", string.IsNullOrWhiteSpace(status) ? "Available" : status);
			sqlCommand2.Parameters.AddWithValue("@SerialNo", text4);
			sqlCommand2.ExecuteNonQuery();
		}
		return new EquipmentRowDto(text3, name.Trim(), text2, text, text4, status, null);
	}

	public List<BookingRowDto> GetBookings(string? customerId = null)
	{
		List<BookingRowDto> list = new List<BookingRowDto>();
		using SqlConnection sqlConnection = CreateConnection();
		sqlConnection.Open();
		using SqlCommand sqlCommand = new SqlCommand("\n            IF OBJECT_ID(N'dbo.EventAppointment', N'U') IS NOT NULL\n            BEGIN\n                SELECT ea.EventAppointmentID,\n                       ea.EventName,\n                       ea.EventDescription,\n                       ea.CustomerID,\n                       COALESCE(c.CustomerFirstName + ' ' + c.CustomerLastName, ea.CustomerID) AS CustomerName,\n                       ea.EventDate,\n                       ea.EventTime,\n                       ea.EventStatus,\n                       ea.CreatedAt\n                FROM dbo.EventAppointment ea\n                LEFT JOIN dbo.Customer c ON c.CustomerID = ea.CustomerID\n                WHERE (@CustomerId IS NULL OR UPPER(ea.CustomerID) = UPPER(@CustomerId) OR UPPER(c.CustomerEmail) = UPPER(@CustomerId))\n                ORDER BY ea.EventDate DESC, ea.EventTime DESC;\n            END", sqlConnection);
		sqlCommand.Parameters.AddWithValue("@CustomerId", ((object)customerId) ?? ((object)DBNull.Value));
		using SqlDataReader sqlDataReader = sqlCommand.ExecuteReader();
		while (sqlDataReader.Read())
		{
			list.Add(new BookingRowDto(sqlDataReader.GetString(0), sqlDataReader.GetString(1), sqlDataReader.IsDBNull(2) ? string.Empty : sqlDataReader.GetString(2), sqlDataReader.IsDBNull(3) ? string.Empty : sqlDataReader.GetString(3), sqlDataReader.IsDBNull(4) ? string.Empty : sqlDataReader.GetString(4), sqlDataReader.GetDateTime(5), sqlDataReader.GetFieldValue<TimeSpan>(6), sqlDataReader.IsDBNull(7) ? "Scheduled" : sqlDataReader.GetString(7), sqlDataReader.IsDBNull(8) ? DateTime.UtcNow : sqlDataReader.GetDateTime(8)));
		}
		return list;
	}

	public void UpdateBookingStatus(string eventAppointmentId, string newStatus)
	{
		if (string.IsNullOrWhiteSpace(eventAppointmentId) || string.IsNullOrWhiteSpace(newStatus))
		{
			return;
		}
		using SqlConnection sqlConnection = CreateConnection();
		sqlConnection.Open();
		using SqlCommand sqlCommand = new SqlCommand("\n            UPDATE dbo.EventAppointment\n            SET EventStatus = @Status\n            WHERE EventAppointmentID = @Id;", sqlConnection);
		sqlCommand.Parameters.AddWithValue("@Id", eventAppointmentId.Trim());
		sqlCommand.Parameters.AddWithValue("@Status", newStatus.Trim());
		sqlCommand.ExecuteNonQuery();
	}

	public List<PhotoRowDto> GetPhotos(string? bookingId = null)
	{
		List<PhotoRowDto> list = new List<PhotoRowDto>();
		using SqlConnection sqlConnection = CreateConnection();
		sqlConnection.Open();
		using SqlCommand sqlCommand = new SqlCommand("\n            IF OBJECT_ID(N'dbo.Photos', N'U') IS NOT NULL\n            BEGIN\n                SELECT Id, BookingId, FileName, StoragePath, UploadedById, UploadedAt, IsWatermarked, IsFinalDelivery\n                FROM dbo.Photos\n                WHERE (@BookingId IS NULL OR BookingId = @BookingId)\n                ORDER BY UploadedAt DESC;\n            END", sqlConnection);
		sqlCommand.Parameters.AddWithValue("@BookingId", ((object)bookingId) ?? ((object)DBNull.Value));
		using SqlDataReader sqlDataReader = sqlCommand.ExecuteReader();
		while (sqlDataReader.Read())
		{
			list.Add(new PhotoRowDto(sqlDataReader.GetInt32(0), sqlDataReader.GetString(1), sqlDataReader.GetString(2), sqlDataReader.GetString(3), sqlDataReader.GetString(4), sqlDataReader.GetDateTime(5), sqlDataReader.GetBoolean(6), sqlDataReader.GetBoolean(7)));
		}
		return list;
	}

	public List<AuditLogRowDto> GetAuditLogs(string? userId = null)
	{
		List<AuditLogRowDto> list = new List<AuditLogRowDto>();
		try
		{
			using SqlConnection sqlConnection = CreateConnection();
			sqlConnection.Open();
			using SqlCommand sqlCommand = new SqlCommand("\n                IF OBJECT_ID(N'dbo.HistoryLog', N'U') IS NOT NULL\n                BEGIN\n                    SELECT TOP 20 HistoryLogID, ActionType, EntityName, EntityID, ChangedBy, ActionDetails, LoggedAt\n                    FROM dbo.HistoryLog\n                    WHERE (@UserId IS NULL OR UPPER(ChangedBy) = UPPER(@UserId))\n                    ORDER BY LoggedAt DESC;\n                END\n                ELSE IF OBJECT_ID(N'dbo.AuditLogs', N'U') IS NOT NULL\n                BEGIN\n                    SELECT TOP 20 Id, Action, EntityType, EntityId, UserId, IpAddress, Timestamp\n                    FROM dbo.AuditLogs\n                    WHERE (@UserId IS NULL OR UPPER(UserId) = UPPER(@UserId))\n                    ORDER BY Timestamp DESC;\n                END", sqlConnection);
			sqlCommand.Parameters.AddWithValue("@UserId", ((object)userId) ?? ((object)DBNull.Value));
			using SqlDataReader sqlDataReader = sqlCommand.ExecuteReader();
			while (sqlDataReader.Read())
			{
				list.Add(new AuditLogRowDto(sqlDataReader.GetInt64(0), sqlDataReader.IsDBNull(1) ? "Action" : sqlDataReader.GetString(1), sqlDataReader.IsDBNull(2) ? "General" : sqlDataReader.GetString(2), sqlDataReader.IsDBNull(3) ? null : sqlDataReader.GetString(3), sqlDataReader.IsDBNull(4) ? null : sqlDataReader.GetString(4), sqlDataReader.IsDBNull(5) ? null : sqlDataReader.GetString(5), sqlDataReader.IsDBNull(6) ? DateTime.UtcNow : sqlDataReader.GetDateTime(6)));
			}
		}
		catch
		{
		}
		return list;
	}

	public IReadOnlyList<AuditLogRowDto> GetHistoryLogs(string requestingUserId, int topCount = 100)
	{
		List<AuditLogRowDto> list = new List<AuditLogRowDto>();
		try
		{
			using SqlConnection sqlConnection = CreateConnection();
			sqlConnection.Open();
			bool flag = !string.IsNullOrWhiteSpace(requestingUserId) && requestingUserId.StartsWith("SADM-", StringComparison.OrdinalIgnoreCase);
			string cmdText = (flag ? "IF OBJECT_ID(N'dbo.HistoryLog', N'U') IS NOT NULL\n                    BEGIN\n                        SELECT TOP (@TopCount) HistoryLogID, ActionType, EntityName, EntityID, ChangedBy, ActionDetails, LoggedAt\n                        FROM dbo.HistoryLog\n                        ORDER BY LoggedAt DESC;\n                    END" : "IF OBJECT_ID(N'dbo.HistoryLog', N'U') IS NOT NULL\n                    BEGIN\n                        SELECT TOP (@TopCount) HistoryLogID, ActionType, EntityName, EntityID, ChangedBy, ActionDetails, LoggedAt\n                        FROM dbo.HistoryLog\n                        WHERE UPPER(ChangedBy) = UPPER(@RequestingUserId)\n                        ORDER BY LoggedAt DESC;\n                    END");
			using SqlCommand sqlCommand = new SqlCommand(cmdText, sqlConnection);
			sqlCommand.Parameters.AddWithValue("@TopCount", topCount);
			if (!flag)
			{
				sqlCommand.Parameters.AddWithValue("@RequestingUserId", requestingUserId.Trim());
			}
			using SqlDataReader sqlDataReader = sqlCommand.ExecuteReader();
			while (sqlDataReader.Read())
			{
				list.Add(new AuditLogRowDto(sqlDataReader.GetInt64(0), sqlDataReader.IsDBNull(1) ? "Action" : sqlDataReader.GetString(1), sqlDataReader.IsDBNull(2) ? "General" : sqlDataReader.GetString(2), sqlDataReader.IsDBNull(3) ? null : sqlDataReader.GetString(3), sqlDataReader.IsDBNull(4) ? null : sqlDataReader.GetString(4), sqlDataReader.IsDBNull(5) ? null : sqlDataReader.GetString(5), sqlDataReader.IsDBNull(6) ? DateTime.UtcNow : sqlDataReader.GetDateTime(6)));
			}
		}
		catch
		{
		}
		return list;
	}
}
