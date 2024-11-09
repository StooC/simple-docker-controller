using System.Collections;
using System.Net.Sockets;
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
    private readonly bool _killSelf;
    private readonly bool _allowList;

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

        var killSelfValue = Environment.GetEnvironmentVariable("KILL_SELF");
        if (!string.IsNullOrEmpty(killSelfValue) && bool.TryParse(killSelfValue, out _killSelf))
            Console.WriteLine($"Kill Self Value set as: {_killSelf}");
        
        var allowListValue = Environment.GetEnvironmentVariable("ALLOW_LIST");
        if (!string.IsNullOrEmpty(allowListValue) && bool.TryParse(allowListValue, out _allowList))
            Console.WriteLine($"Allow List Value set as: {_allowList}");
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
            var containers = await GetAllRunningContainers();

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

            if (!_killSelf)
            {
                Console.WriteLine("Stopped all other containers and requested self shutdown.");
                return Ok("Stopped all other containers and requested self shutdown.");
            }

            Console.WriteLine("Self kill requested");
            await StopOrKillContainer(currentContainerId);
            return Ok("Stopped all other containers and self killed."); // this line should not execute as container killed
        }
        catch (Exception ex)
        {
            if (ex.InnerException is SocketException innerException)
                return StatusCode(500, $"Socket Error: {innerException.Message}");
            
            return StatusCode(500, $"Error shutting down all containers: {ex.Message}");
        }
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
        if (!_allowList)
        {
            Console.WriteLine($"Access to listing containers is disabled in configuration.");
            throw new UnauthorizedAccessException();
        }

        try
        {
            var containers = await GetAllRunningContainers();
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
            if (ex.InnerException is SocketException innerException)
                return StatusCode(500, $"Socket Error: {innerException.Message}");
            
            return StatusCode(500, $"Error listing containers: {ex.Message}");
        }
    }
    
    private async Task<IList<ContainerListResponse>> GetAllRunningContainers()
        => await _dockerClient.Containers.ListContainersAsync(new ContainersListParameters() { All = false });
    
    private async Task StopOrKillContainer(string containerId)
    {
        await _dockerClient.Containers.StopContainerAsync(containerId, new ContainerStopParameters()
        {
            WaitBeforeKillSeconds = _waitBeforeKill
        });
    }
}