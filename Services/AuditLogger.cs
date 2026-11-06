using System.Data.SqlClient;

public class AuditLogger
{
    private string connectionString = "Data Source=YOUR_SERVER;Initial Catalog=YOUR_DB;Integrated Security=True";

    public void LogExecution(string user, string target, string command, string status, string output)
    {
        using (SqlConnection conn = new SqlConnection(connectionString))
        {
            string query = "INSERT INTO ExecutionAuditLogs (InitiatedBy, TargetNode, CommandText, Status, OutputPreview) VALUES (@user, @target, @cmd, @status, @output)";
            SqlCommand cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@user", user);
            cmd.Parameters.AddWithValue("@target", target);
            cmd.Parameters.AddWithValue("@cmd", command);
            cmd.Parameters.AddWithValue("@status", status);
            
            string preview = output.Length > 500 ? output.Substring(0, 500) + "..." : output;
            cmd.Parameters.AddWithValue("@output", preview);

            conn.Open();
 















           cmd.ExecuteNonQuery();
        }


    }

}
