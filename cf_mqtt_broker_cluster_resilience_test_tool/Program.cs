using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;


namespace Coreflux.Tool;

class Program
{
    private static readonly ConcurrentDictionary<string, (int? Previous, int? Actual)> LastValues = new();
    private static int SequenceErrorCount = 0;

    public static async Task Main(string[] args)
    {
        string broker = GetValidInput("Broker Address: ");
        int port = GetValidIntInput("Port: ");
        string topicPrefix = GetValidInput("Topic prefix: ");
        int clientCount = GetValidIntInput("Number of clients: ");
        int publishTimeMs = GetValidIntInput("Publish time (ms): ");
        int watchTimeSec = GetValidIntInput("Run time (s): ");

        Console.WriteLine($"\n[{DateTime.Now.Hour}:{DateTime.Now.Minute}:{DateTime.Now.Second}]: broker ip: {broker}:{port}, topic: {topicPrefix}\n");

        var cts = new CancellationTokenSource();
        var elapsedSeconds = Stopwatch.StartNew();
        try
        {
            Task clientSubscriber = Task.Run(() => StartSubscriber(broker, port, clientCount, topicPrefix, publishTimeMs, cts));

            var timer = new PeriodicTimer(TimeSpan.FromSeconds(watchTimeSec));
            while (await timer.WaitForNextTickAsync(cts.Token))
            {
                cts.Cancel();
                break;
            }

            elapsedSeconds.Stop();

            Console.WriteLine($"\n[{DateTime.Now.Hour}:{DateTime.Now.Minute}:{DateTime.Now.Second}]: Finished with no payload loss. Elapsed time: {elapsedSeconds.Elapsed}\n");
        }
        catch (Exception)
        {
            Console.WriteLine($"\n[{DateTime.Now.Hour}:{DateTime.Now.Minute}:{DateTime.Now.Second}]: Disconnecting clients...");
        }
        finally
        {
            Console.WriteLine("\nPress any key to close");
            Console.ReadKey();
        }
    }

    private static string GetValidInput(string prompt)
    {
        string input;
        do
        {
            Console.Write(prompt);
            input = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(input))
                Console.WriteLine("Invalid value. Try again.");
        } while (string.IsNullOrWhiteSpace(input));

        return input;
    }

    private static int GetValidIntInput(string prompt)
    {
        int value;
        do
        {
            Console.Write(prompt);
            string input = Console.ReadLine();
            if (int.TryParse(input, out value))
                return value;

            Console.WriteLine($"Invalid value. Please enter a valid integer.");
        } while (true);
    }

#region publisher

    private static async void StartPublisher(string clientId, int timeMs, string broker, int port, string topicPrefix, CancellationToken cancellationToken)
    {
        IMqttClient mqttClient;

        var options = new MqttClientOptionsBuilder()
            .WithTcpServer(broker, port)
            .Build();

        mqttClient = new MqttFactory().CreateMqttClient();

        try
        {
            await mqttClient.ConnectAsync(options, CancellationToken.None);
            Console.WriteLine($"[{DateTime.Now.Hour}:{DateTime.Now.Minute}:{DateTime.Now.Second}]: Publisher {clientId} connected.");

            string topic = $"{topicPrefix}{clientId}";


            int i = 0;
            while (!cancellationToken.IsCancellationRequested)
            {
                int payload = i % 10; // 0 - 9

                /* testing the subscribers response to errors */
                // if (clientId == "43" && i == 200)
                //     payload = 12;

                var message = new MqttApplicationMessageBuilder()
                    .WithTopic(topic)
                    .WithPayload(payload.ToString())
                    .Build();

                await mqttClient.PublishAsync(message, CancellationToken.None);
                i++;

                await Task.Delay(timeMs, cancellationToken);
            }
        }
        catch (TaskCanceledException)
        {
            // Console.WriteLine($"{clientId} stopped");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n\n[{DateTime.Now.Hour}:{DateTime.Now.Minute}:{DateTime.Now.Second}]: Error: {ex.Message}");
        }
        finally
        {
            await mqttClient.DisconnectAsync();
            // Console.WriteLine($"Publisher {clientId} disconnected.");
        }
    }

#endregion

#region subscriber

    private static async Task StartSubscriber(string broker, int port, int clientCount, string topicPrefix, int publishTimeMs, CancellationTokenSource cts)
    {
        var options = new MqttClientOptionsBuilder()
            .WithTcpServer(broker, port)
            .Build();

        var mqttClient = new MqttFactory().CreateMqttClient();

        mqttClient.ConnectedAsync += async e =>
        {
            Console.WriteLine($"[{DateTime.Now.Hour}:{DateTime.Now.Minute}:{DateTime.Now.Second}]: Subscriber connected.");

            // Subscribe to all publisher topics
            for (int i = 1; i <= clientCount; i++)
            {
                string topic = $"{topicPrefix}{i}";
                await mqttClient.SubscribeAsync(topic);
                Console.WriteLine($"[{DateTime.Now.Hour}:{DateTime.Now.Minute}:{DateTime.Now.Second}]: Subscriber subscribed to {topic}.");
            }
        };

        mqttClient.ApplicationMessageReceivedAsync += async e =>
       {
           string topic = e.ApplicationMessage.Topic;
           string payload = Encoding.UTF8.GetString(e.ApplicationMessage.PayloadSegment);

           try
           {
               int value = int.Parse(payload);
               var lastValues = LastValues.GetOrAdd(topic, _ => (null, null));

               if (lastValues.Previous == null)
                   LastValues[topic] = (value, null);

               else if (lastValues.Actual == null)
                   LastValues[topic] = (lastValues.Previous, value);

               else
               {
                   // Shift values and validate sequence
                   int expectedValue = (lastValues.Previous.Value + 1) % 10;

                   if (lastValues.Actual.Value != expectedValue)
                   {
                       Console.WriteLine($"\n\n[{DateTime.Now.Hour}:{DateTime.Now.Minute}:{DateTime.Now.Second}]: Sequence error on {topic}: Previous = {lastValues.Previous}, Actual = {lastValues.Actual}");
                       Interlocked.Increment(ref SequenceErrorCount);
                       cts.Cancel();
                   }

                   LastValues[topic] = (lastValues.Actual, value);
               }
           }
           catch (FormatException)
           {
               Console.WriteLine($"[{DateTime.Now.Hour}:{DateTime.Now.Minute}:{DateTime.Now.Second}]: Non-integer value received on {topic}: {payload}");
               Interlocked.Increment(ref SequenceErrorCount);
           }

           await Task.CompletedTask;
       };

        await mqttClient.ConnectAsync(options, CancellationToken.None);

        try
        {
            Task[] clientPublish = new Task[clientCount];
            for (int i = 0; i < clientCount; i++)
            {
                string clientId = $"{i + 1}";
                clientPublish[i] = Task.Run(() => StartPublisher(clientId, publishTimeMs, broker, port, topicPrefix, cts.Token));
            }
            await Task.WhenAll(clientPublish);

            while (!cts.IsCancellationRequested)
            {
                await Task.Delay(100, cts.Token);
            }
        }
        catch (TaskCanceledException)
        { }
        finally
        {
            await mqttClient.DisconnectAsync();
        }
    }
#endregion
}