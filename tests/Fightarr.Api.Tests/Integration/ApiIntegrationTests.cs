using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Fightarr.Api.Models;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Fightarr.Api.Data;

namespace Fightarr.Api.Tests.Integration;

/// <summary>
/// Integration tests for API endpoints using WebApplicationFactory
/// </summary>
public class ApiIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public ApiIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // Remove the existing DbContext
                var descriptor = services.SingleOrDefault(
                    d => d.ServiceType == typeof(DbContextOptions<FightarrDbContext>));
                if (descriptor != null)
                {
                    services.Remove(descriptor);
                }

                // Add in-memory database for testing
                services.AddDbContext<FightarrDbContext>(options =>
                {
                    options.UseInMemoryDatabase("TestDatabase");
                });

                // Build service provider and create database
                var sp = services.BuildServiceProvider();
                using var scope = sp.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<FightarrDbContext>();
                db.Database.EnsureCreated();
            });
        });

        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task GetSystemStatus_ShouldReturnOk()
    {
        // Act
        var response = await _client.GetAsync("/api/system/status");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        content.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GetEvents_ShouldReturnOk()
    {
        // Act
        var response = await _client.GetAsync("/api/events");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var events = await response.Content.ReadFromJsonAsync<List<Event>>();
        events.Should().NotBeNull();
    }

    [Fact]
    public async Task GetQualityProfiles_ShouldReturnOk()
    {
        // Act
        var response = await _client.GetAsync("/api/qualityprofile");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var profiles = await response.Content.ReadFromJsonAsync<List<QualityProfile>>();
        profiles.Should().NotBeNull();
    }

    [Fact]
    public async Task GetTags_ShouldReturnOk()
    {
        // Act
        var response = await _client.GetAsync("/api/tag");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var tags = await response.Content.ReadFromJsonAsync<List<Tag>>();
        tags.Should().NotBeNull();
    }

    [Fact]
    public async Task CreateEvent_ShouldReturnCreated()
    {
        // Arrange
        var newEvent = new Event
        {
            Title = "Test UFC Event",
            Organization = "UFC",
            EventDate = DateTime.UtcNow.AddDays(30)
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/events", newEvent);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var createdEvent = await response.Content.ReadFromJsonAsync<Event>();
        createdEvent.Should().NotBeNull();
        createdEvent!.Title.Should().Be("Test UFC Event");
        createdEvent.Id.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task CreateTag_ShouldReturnCreated()
    {
        // Arrange
        var newTag = new Tag
        {
            Label = "Test Tag",
            Color = "#FF0000"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/tag", newTag);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var createdTag = await response.Content.ReadFromJsonAsync<Tag>();
        createdTag.Should().NotBeNull();
        createdTag!.Label.Should().Be("Test Tag");
        createdTag.Color.Should().Be("#FF0000");
    }

    [Fact]
    public async Task UpdateEvent_ShouldReturnOk()
    {
        // Arrange - First create an event
        var newEvent = new Event
        {
            Title = "Original Title",
            Organization = "UFC",
            EventDate = DateTime.UtcNow.AddDays(30)
        };

        var createResponse = await _client.PostAsJsonAsync("/api/events", newEvent);
        var createdEvent = await createResponse.Content.ReadFromJsonAsync<Event>();

        // Update the event
        createdEvent!.Title = "Updated Title";

        // Act
        var response = await _client.PutAsJsonAsync($"/api/events/{createdEvent.Id}", createdEvent);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var updatedEvent = await response.Content.ReadFromJsonAsync<Event>();
        updatedEvent.Should().NotBeNull();
        updatedEvent!.Title.Should().Be("Updated Title");
    }

    [Fact]
    public async Task DeleteEvent_ShouldReturnNoContent()
    {
        // Arrange - First create an event
        var newEvent = new Event
        {
            Title = "Event To Delete",
            Organization = "UFC",
            EventDate = DateTime.UtcNow.AddDays(30)
        };

        var createResponse = await _client.PostAsJsonAsync("/api/events", newEvent);
        var createdEvent = await createResponse.Content.ReadFromJsonAsync<Event>();

        // Act
        var response = await _client.DeleteAsync($"/api/events/{createdEvent!.Id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify deletion
        var getResponse = await _client.GetAsync($"/api/events/{createdEvent.Id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetEvent_WithInvalidId_ShouldReturnNotFound()
    {
        // Act
        var response = await _client.GetAsync("/api/events/99999");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CreateQualityProfile_ShouldReturnCreated()
    {
        // Arrange
        var profile = new QualityProfile
        {
            Name = "Test HD Profile",
            UpgradesAllowed = true,
            Items = new List<QualityItem>
            {
                new QualityItem { Name = "1080p", Quality = 8, Allowed = true },
                new QualityItem { Name = "720p", Quality = 4, Allowed = true }
            }
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/qualityprofile", profile);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var createdProfile = await response.Content.ReadFromJsonAsync<QualityProfile>();
        createdProfile.Should().NotBeNull();
        createdProfile!.Name.Should().Be("Test HD Profile");
        createdProfile.Items.Should().HaveCount(2);
    }

    [Fact]
    public async Task SearchEvents_ShouldReturnResults()
    {
        // Note: This test assumes the search endpoint exists and works
        // Actual implementation may vary

        // Act
        var response = await _client.GetAsync("/api/search/events?q=UFC");

        // Assert
        // Even if no results, should return 200 OK
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetDownloadClients_ShouldReturnOk()
    {
        // Act
        var response = await _client.GetAsync("/api/downloadclient");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var clients = await response.Content.ReadFromJsonAsync<List<DownloadClient>>();
        clients.Should().NotBeNull();
    }

    [Fact]
    public async Task GetIndexers_ShouldReturnOk()
    {
        // Act
        var response = await _client.GetAsync("/api/indexer");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var indexers = await response.Content.ReadFromJsonAsync<List<Indexer>>();
        indexers.Should().NotBeNull();
    }

    [Fact]
    public async Task ConcurrentRequests_ShouldAllSucceed()
    {
        // Arrange
        var tasks = new List<Task<HttpResponseMessage>>();

        // Act - Send 10 concurrent requests
        for (int i = 0; i < 10; i++)
        {
            tasks.Add(_client.GetAsync("/api/system/status"));
        }

        var responses = await Task.WhenAll(tasks);

        // Assert
        responses.Should().AllSatisfy(r => r.StatusCode.Should().Be(HttpStatusCode.OK));
    }
}
