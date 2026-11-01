using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using XNotch.Models;

namespace XNotch.Persistence;

/// <summary>
/// Embedded SQLite persistence manager providing sub-millisecond local storage
/// for discovered projects, task backlogs, smart reminders, and session history.
/// </summary>
public sealed class XNotchDatabase : IDisposable
{
    private static readonly string DbPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "XNotch", "xnotch.db");

    private readonly string _connectionString;
    private readonly object _lock = new();

    public XNotchDatabase()
    {
        string dir = Path.GetDirectoryName(DbPath)!;
        Directory.CreateDirectory(dir);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();

        InitializeSchema();
    }

    private void InitializeSchema()
    {
        lock (_lock)
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                PRAGMA journal_mode = WAL;
                PRAGMA synchronous = NORMAL;

                CREATE TABLE IF NOT EXISTS projects (
                    id TEXT PRIMARY KEY,
                    name TEXT NOT NULL,
                    path TEXT NOT NULL UNIQUE,
                    tech_stack TEXT,
                    git_branch TEXT,
                    has_uncommitted INTEGER,
                    last_modified TEXT,
                    description TEXT,
                    last_accessed TEXT
                );

                CREATE TABLE IF NOT EXISTS tasks (
                    id TEXT PRIMARY KEY,
                    project_id TEXT,
                    project_name TEXT,
                    title TEXT NOT NULL,
                    source_evidence TEXT,
                    priority INTEGER,
                    status INTEGER,
                    discovered_at TEXT,
                    due_date TEXT,
                    suggested_action TEXT
                );

                CREATE TABLE IF NOT EXISTS reminders (
                    id TEXT PRIMARY KEY,
                    title TEXT NOT NULL,
                    reason TEXT,
                    scheduled_time TEXT,
                    is_dismissed INTEGER,
                    project_path TEXT
                );

                CREATE TABLE IF NOT EXISTS agent_history (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    agent_id TEXT NOT NULL,
                    display_name TEXT,
                    pid INTEGER,
                    project_path TEXT,
                    git_branch TEXT,
                    phase INTEGER,
                    started_at TEXT,
                    duration_sec REAL
                );
            ";
            cmd.ExecuteNonQuery();
        }
    }

    public async Task UpsertProjectAsync(ProjectRecord project)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO projects (id, name, path, tech_stack, git_branch, has_uncommitted, last_modified, description, last_accessed)
            VALUES (@id, @name, @path, @tech, @branch, @uncommitted, @mod, @desc, @accessed)
            ON CONFLICT(id) DO UPDATE SET
                tech_stack = excluded.tech_stack,
                git_branch = excluded.git_branch,
                has_uncommitted = excluded.has_uncommitted,
                last_modified = excluded.last_modified,
                description = excluded.description,
                last_accessed = excluded.last_accessed;
        ";
        string normPath = Path.GetFullPath(project.Path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToLowerInvariant();
        cmd.Parameters.AddWithValue("@id", project.Id);
        cmd.Parameters.AddWithValue("@name", project.Name);
        cmd.Parameters.AddWithValue("@path", normPath);
        cmd.Parameters.AddWithValue("@tech", (object?)project.TechStack ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@branch", (object?)project.GitBranch ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@uncommitted", project.HasUncommittedChanges ? 1 : 0);
        cmd.Parameters.AddWithValue("@mod", project.LastModified.ToString("o"));
        cmd.Parameters.AddWithValue("@desc", (object?)project.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@accessed", DateTime.UtcNow.ToString("o"));

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<ProjectRecord>> GetAllProjectsAsync()
    {
        var list = new List<ProjectRecord>();
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, name, path, tech_stack, git_branch, has_uncommitted, last_modified, description FROM projects ORDER BY last_accessed DESC;";

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new ProjectRecord(
                Id: reader.GetString(0),
                Name: reader.GetString(1),
                Path: reader.GetString(2),
                TechStack: reader.IsDBNull(3) ? "Generic" : reader.GetString(3),
                GitBranch: reader.IsDBNull(4) ? "main" : reader.GetString(4),
                HasUncommittedChanges: reader.GetInt32(5) == 1,
                LastModified: DateTime.TryParse(reader.GetString(6), out var dt) ? dt : DateTime.UtcNow,
                Description: reader.IsDBNull(7) ? null : reader.GetString(7)
            ));
        }

        return list;
    }

    public async Task UpsertTaskAsync(TaskItem task)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO tasks (id, project_id, project_name, title, source_evidence, priority, status, discovered_at, due_date, suggested_action)
            VALUES (@id, @projId, @projName, @title, @evidence, @priority, @status, @disc, @due, @act)
            ON CONFLICT(id) DO UPDATE SET
                status = excluded.status,
                due_date = excluded.due_date,
                suggested_action = excluded.suggested_action;
        ";
        cmd.Parameters.AddWithValue("@id", task.Id);
        cmd.Parameters.AddWithValue("@projId", task.ProjectId);
        cmd.Parameters.AddWithValue("@projName", task.ProjectName);
        cmd.Parameters.AddWithValue("@title", task.Title);
        cmd.Parameters.AddWithValue("@evidence", task.SourceEvidence);
        cmd.Parameters.AddWithValue("@priority", (int)task.Priority);
        cmd.Parameters.AddWithValue("@status", (int)task.Status);
        cmd.Parameters.AddWithValue("@disc", task.DiscoveredAt.ToString("o"));
        cmd.Parameters.AddWithValue("@due", task.DueDate.HasValue ? task.DueDate.Value.ToString("o") : DBNull.Value);
        cmd.Parameters.AddWithValue("@act", (object?)task.SuggestedNextAction ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task RecordAgentSessionAsync(AgentSession session)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO agent_history (agent_id, display_name, pid, project_path, git_branch, phase, started_at, duration_sec)
            VALUES (@agent, @name, @pid, @path, @branch, @phase, @start, @dur);
        ";
        cmd.Parameters.AddWithValue("@agent", session.AgentId);
        cmd.Parameters.AddWithValue("@name", session.DisplayName);
        cmd.Parameters.AddWithValue("@pid", session.ProcessId);
        cmd.Parameters.AddWithValue("@path", session.ProjectPath);
        cmd.Parameters.AddWithValue("@branch", session.GitBranch);
        cmd.Parameters.AddWithValue("@phase", (int)session.Phase);
        cmd.Parameters.AddWithValue("@start", session.StartedAt.ToString("o"));
        cmd.Parameters.AddWithValue("@dur", session.ElapsedDuration.TotalSeconds);

        await cmd.ExecuteNonQueryAsync();
    }

    public void Dispose()
    {
    }
}
