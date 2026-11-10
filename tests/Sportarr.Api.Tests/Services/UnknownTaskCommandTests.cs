using System.Reflection;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using AppTaskStatus = Sportarr.Api.Models.TaskStatus;

namespace Sportarr.Api.Tests.Services;

public class UnknownTaskCommandTests
{
    [Theory]
    [InlineData("RescanSeries")]
    [InlineData("DownloadedEpisodesScan")]
    [InlineData("MadeUpCommand")]
    public async Task Unknown_task_command_fails_instead_of_completing(string commandName)
    {
        var services = new ServiceCollection();
        var databaseName = Guid.NewGuid().ToString();
        services.AddDbContext<SportarrDbContext>(options =>
            options.UseInMemoryDatabase(databaseName));
        using var provider = services.BuildServiceProvider();
        var taskService = new TaskService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<TaskService>.Instance);
        int taskId;
        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SportarrDbContext>();
            var task = new AppTask { Name = commandName, CommandName = commandName };
            db.Tasks.Add(task);
            await db.SaveChangesAsync();
            taskId = task.Id;
        }

        var execute = typeof(TaskService).GetMethod("ExecuteTaskAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);
        execute.Should().NotBeNull("the test drives the private task runner");
        await (Task)execute!.Invoke(taskService, new object[] { taskId })!;

        var result = await taskService.GetTaskAsync(taskId);
        result!.Status.Should().Be(AppTaskStatus.Failed);
        result.Message.Should().Contain(commandName);
        result.Exception.Should().Contain(nameof(NotSupportedException));
    }
}
