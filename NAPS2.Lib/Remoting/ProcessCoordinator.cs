﻿using System.Text;
using GrpcDotNetNamedPipes;
using static NAPS2.Remoting.ProcessCoordinatorService;

namespace NAPS2.Remoting;

/// <summary>
/// Manages communication and coordination between multiple NAPS2 GUI processes. Specifically:
/// - Allows sending messages to other NAPS2 processes via named pipes
/// - Allows taking the SingleInstance lock (or checking which process currently owns it)
/// This is different than the worker service - workers are owned by the parent process and are considered part of the
/// same unit. Instead, this class handles the case where the user (or a system feature like StillImage) opens NAPS2
/// twice.
/// </summary>
public class ProcessCoordinator(string instanceLockPath, string pipeNameFormat)
{
    public static ProcessCoordinator CreateDefault() =>
        new(Path.Combine(Paths.AppData, "instance.lock"), "NAPS2_PIPE_v2_{0}");

    private NamedPipeServer? _server;
    private FileStream? _instanceLock;

    private string GetPipeName(Process process)
    {
        return string.Format(pipeNameFormat, process.Id);
    }

    public void StartServer(ProcessCoordinatorServiceBase service)
    {
        _server = new NamedPipeServer(GetPipeName(Process.GetCurrentProcess()));
        ProcessCoordinatorService.BindService(_server.ServiceBinder, service);
        _server.Start();
    }

    public void KillServer()
    {
        _server?.Kill();
    }

    private ProcessCoordinatorServiceClient GetClient(Process recipient, int timeout) =>
        new(new NamedPipeChannel(".", GetPipeName(recipient),
            new NamedPipeChannelOptions { ConnectionTimeout = timeout }));

    private bool TrySendMessage(Process recipient, int timeout, Action<ProcessCoordinatorServiceClient> send)
    {
        var client = GetClient(recipient, timeout);
        try
        {
            send(client);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public bool Activate(Process recipient, int timeout) =>
        TrySendMessage(recipient, timeout, client => client.Activate(new ActivateRequest()));

    public bool CloseWindow(Process recipient, int timeout) =>
        TrySendMessage(recipient, timeout, client => client.CloseWindow(new CloseWindowRequest()));

    public bool ScanWithDevice(Process recipient, int timeout, string device) =>
        TrySendMessage(recipient, timeout,
            client => client.ScanWithDevice(new ScanWithDeviceRequest { Device = device }));

    public bool OpenFile(Process recipient, int timeout, string path) =>
        TrySendMessage(recipient, timeout,
            client => client.OpenFile(new OpenFileRequest { Path = { path } }));

    public bool TryTakeInstanceLock()
    {
        if (_instanceLock != null)
        {
            return true;
        }
        try
        {
            _instanceLock = new FileStream(instanceLockPath, FileMode.Create, FileAccess.ReadWrite, FileShare.Read);
            _instanceLock.SetLength(0);
            using var writer = new StreamWriter(_instanceLock, Encoding.UTF8, 1024, true);
            writer.WriteLine(Process.GetCurrentProcess().Id);
        }
        catch (Exception)
        {
            return false;
        }
        return true;
    }

    public Process? GetProcessWithInstanceLock()
    {
        try
        {
            using var reader = new FileStream(instanceLockPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var id = int.Parse(new StreamReader(reader).ReadLine()?.Trim() ?? "");
            return Process.GetProcessById(id);
        }
        catch (Exception)
        {
            return null;
        }
    }
}