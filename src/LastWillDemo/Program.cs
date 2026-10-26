// MQ02 demo: a sensor that leaves a Last Will, an agent that reads presence before values,
// and a cable between the sensor and the broker that can be cut like a power line.
//
//   dotnet run -- agent                    the AI agent, subscribed to line3/#
//   dotnet run -- sensor                   K = kill the process, C = cut the cable, Q = clean goodbye
//   dotnet run -- sensor --no-will         the trap: a retained "online" with nothing to overwrite it
//   dotnet run -- sensor --then cut --after 6 --for 40   scripted, for recordings
using MQTTnet;
using MQTTnet.Formatter;
using MQTTnet.Protocol;

var opt = Options.Parse(args);
var factory = new MqttClientFactory();
var clock = System.Diagnostics.Stopwatch.StartNew();
Console.OutputEncoding = System.Text.Encoding.UTF8;

await (opt.Mode switch
{
    "sensor" => RunSensor(),
    "agent" => RunAgent(),
    _ => throw new ArgumentException("usage: sensor | agent"),
});

async Task RunSensor()
{
    await using var cable = Cable.PlugIn(listenPort: 1884, opt.Broker, brokerPort: 1883);

    var builder = new MqttClientOptionsBuilder()
        .WithTcpServer("127.0.0.1", 1884)                 // through the cable, so we can cut it
        .WithClientId("line3-sensor")
        .WithProtocolVersion(MqttProtocolVersion.V500)
        .WithKeepAlivePeriod(TimeSpan.FromSeconds(10));

    if (opt.Will)
    {
        builder
            .WithWillTopic("line3/sensor/status")         // the same topic as "online"
            .WithWillPayload("offline")
            .WithWillRetain()                             // so a late subscriber sees it too
            .WithWillQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
            .WithWillDelayInterval(opt.WillDelay)
            .WithSessionExpiryInterval(opt.SessionExpiry);
    }

    using var sensor = factory.CreateMqttClient();
    var leaving = false;
    sensor.DisconnectedAsync += _ =>
    {
        if (!leaving) Log("sensor", "the client itself just noticed the connection is gone");
        return Task.CompletedTask;
    };

    await sensor.ConnectAsync(builder.Build());
    await Publish(sensor, "line3/sensor/status", "online");
    Log("sensor", opt.Will ? "connected with a Last Will, status = online" : "connected, NO Last Will, status = online");

    var stopAt = opt.Duration is { } d ? clock.Elapsed + d : TimeSpan.MaxValue;
    while (clock.Elapsed < stopAt)
    {
        switch (NextAction())
        {
            case "kill":
                Log("sensor", "process killed, the OS closes the socket");
                Environment.Exit(137);
                break;
            case "cut":
                cable.Cut();
                Log("sensor", "power cut: the socket stays open, nothing crosses it");
                break;
            case "quit":
                leaving = true;
                await Publish(sensor, "line3/sensor/status", "offline");
                await sensor.DisconnectAsync(new MqttClientDisconnectOptionsBuilder()
                    .WithReason(MqttClientDisconnectOptionsReason.NormalDisconnection)
                    .Build());
                Log("sensor", "clean goodbye, the broker drops the Will");
                return;
        }

        if (!cable.IsCut && sensor.IsConnected)
            await Publish(sensor, "line3/temp", "71.0");
        await Task.Delay(TimeSpan.FromSeconds(2));
    }
}

async Task RunAgent()
{
    string status = "unknown", temp = "?", lastAnswer = "";
    var lastReading = TimeSpan.Zero;

    using var agent = factory.CreateMqttClient();
    agent.ApplicationMessageReceivedAsync += e =>
    {
        var message = e.ApplicationMessage;
        var value = message.ConvertPayloadToString();
        Log("agent", $"{message.Topic} = {value}{(message.Retain ? "   (retained)" : "")}");

        if (message.Topic == "line3/temp")
        {
            temp = value;
            lastReading = clock.Elapsed;
        }
        else if (message.Topic == "line3/sensor/status")
        {
            if (value == "offline" && lastReading > TimeSpan.Zero)
                Log("agent", $"offline arrived {(clock.Elapsed - lastReading).TotalSeconds:0.0} s after the last reading");
            status = value;
        }

        // Presence first, then the value.
        var answer = status == "offline"
            ? $"Line 3: I don't know. The sensor is offline, so {temp} means nothing."
            : $"Line 3 is at {temp} °C. All good.";
        if (temp != "?" && answer != lastAnswer)
            Log("agent", $">> {lastAnswer = answer}");
        return Task.CompletedTask;
    };

    await agent.ConnectAsync(new MqttClientOptionsBuilder()
        .WithTcpServer(opt.Broker, 1883)
        .WithClientId("line3-agent")
        .WithProtocolVersion(MqttProtocolVersion.V500)
        .Build());
    await agent.SubscribeAsync(factory.CreateSubscribeOptionsBuilder().WithTopicFilter("line3/#").Build());
    Log("agent", "subscribed to line3/#");

    await Task.Delay(opt.Duration ?? Timeout.InfiniteTimeSpan);
}

Task Publish(IMqttClient client, string topic, string payload) =>
    client.PublishAsync(new MqttApplicationMessageBuilder()
        .WithTopic(topic)
        .WithPayload(payload)
        .WithRetainFlag()                                  // current state, not history
        .Build());

string? NextAction()
{
    if (opt.Then is { } then && clock.Elapsed >= opt.After)
    {
        opt = opt with { Then = null };
        return then;
    }
    if (!Console.IsInputRedirected && Console.KeyAvailable)
    {
        return Console.ReadKey(intercept: true).Key switch
        {
            ConsoleKey.K => "kill",
            ConsoleKey.C => "cut",
            ConsoleKey.Q => "quit",
            _ => null,
        };
    }
    return null;
}

static void Log(string who, string text) =>
    Console.WriteLine($"{DateTime.Now:HH:mm:ss.f}  {who,-6} | {text}");
