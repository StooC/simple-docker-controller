using System.Collections;
using Docker.DotNet;
using Docker.DotNet.Models;
using Microsoft.AspNetCore.Mvc;

namespace SimpleDockerController.Controllers;

/// <summary>
/// REST endpoint to control docker operations using Docker Socket
/// </summary>
[ApiController]
[Route("[controller]")]
[Produces("application/json")]
public class DockerController : ControllerBase
{
    private readonly DockerClient _dockerClient;
    private readonly IHostApplicationLifetime _appLifetime;
    private readonly string _ignoreContainerName;
    private readonly uint? _waitBeforeKill;

    /// <inheritdoc />
    public DockerController(IHostApplicationLifetime appLifetime)
    {
        _appLifetime = appLifetime;
        
        var dockerUri = Environment.GetEnvironmentVariable("DOCKER_SOCK_URI") ?? "unix:///var/run/docker.sock";
        _dockerClient = new DockerClientConfiguration(new Uri(dockerUri)).CreateClient();
        Console.WriteLine($"Docker client configured with Docker Uri: {dockerUri}");
        
        _ignoreContainerName = Environment.GetEnvironmentVariable("IGNORE_CONTAINER_NAME") ?? "simple-docker-controller";
        Console.WriteLine($"Docker ignore container: {_ignoreContainerName}");
        
        _waitBeforeKill = uint.Parse(Environment.GetEnvironmentVariable("WAIT_BEFORE_KILL") ?? "30");
        Console.WriteLine($"Wait before killing container: {_waitBeforeKill} seconds");
    }
        
    // POST /shutdown/all
    /// <summary>
    /// Shuts down all containers currently running except it's self then gracefully exits to its own container also ends
    /// </summary>
    /// <returns>Status message indicating success or failure to shut down containers</returns>
    [HttpPost("shutdown/all")]
    [ProducesResponseType(typeof(string), 200)]
    [ProducesResponseType(500)]
    public async Task<IActionResult> ShutdownAllContainers()
    {
        try
        {
            var containers = await _dockerClient.Containers.ListContainersAsync(new ContainersListParameters() { All = true });

            // Find the container ID of this application
            var currentContainerId = containers
                .FirstOrDefault(c => c.Names.Any(n => n.Contains(_ignoreContainerName)) || c.Image.Contains("simple-docker-controller"))
                ?.ID;
                
            foreach (var container in containers)
            {
                if (container.ID == currentContainerId) 
                    continue;
                
                await StopOrKillContainer(container.ID);
                Console.WriteLine($"Stopped container {container.ID}");
            }
                
            _appLifetime.StopApplication(); // This doesn't seem to actually stop the whole container running

            if (currentContainerId == null) 
                return Ok("Stopped all other containers but could not self shutdown.");
            
            await StopOrKillContainer(currentContainerId);
            return Ok("Stopped all other containers and requested self shutdown.");
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Error shutting down all containers: {ex.Message}");
        }
    }

    private async Task StopOrKillContainer(string containerId)
    {
        await _dockerClient.Containers.StopContainerAsync(containerId, new ContainerStopParameters()
        {
            WaitBeforeKillSeconds = _waitBeforeKill
        });
    }

    // GET api/docker/list
    /// <summary>
    /// Lists all containers known to the docker instance
    /// </summary>
    /// <returns>Full details or a simplified list of all containers depending on configuration</returns>
    [HttpGet("list")]
    [ProducesResponseType(typeof(IEnumerable), 200)]
    [ProducesResponseType(500)]
    public async Task<IActionResult> ListContainers()
    {
        try
        {
            var containers = await _dockerClient.Containers.ListContainersAsync(new ContainersListParameters() { All = true });
            var simpleList = Environment.GetEnvironmentVariable("SIMPLE_LIST")?.ToLower() == "true";

            if (!simpleList) 
                return Ok(containers);
            
            var result = containers.Select(c => new
            {
                Name = c.Names.FirstOrDefault(),
                c.State
            });
            return Ok(result);

        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Error listing containers: {ex.Message}");
        }
    }
}