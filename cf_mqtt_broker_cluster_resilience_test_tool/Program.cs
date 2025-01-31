using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Client.Internal;
using MQTTnet.Packets;
using MQTTnet.Protocol;


namespace Coreflux.Tool;

class Program
{
    private static readonly ConcurrentDictionary<string, (int? Previous, int? Actual)> LastValues = new();
    private static int SequenceErrorCount = 0;

    public static async Task Main(string[] args)
    {
        string broker;
        int port;
        string topicPrefix;
        int clientCount;
        int publishTimeMs;
        int watchTimeSec;
        int behaviour;

        if (args.Length < 1)
        {
            broker = GetValidInput("Broker Address: ");
            port = GetValidIntInput("Port: ");
            topicPrefix = GetValidInput("Topic prefix: ");
            clientCount = GetValidIntInput("Number of clients: ");
            publishTimeMs = GetValidIntInput("Publish time (ms): ");
            watchTimeSec = GetValidIntInput("Run time (s): ");
            behaviour = GetValidBehaviourInput("Select a behaviour option: \n\n(1) Stop at the first missed payload\n(2) Count the missed payload\n\nOption: ");
        }
        else if (args.Length == 7)
        {
            broker = args[0];
            port = int.Parse(args[1]);
            topicPrefix = args[2];
            clientCount = int.Parse(args[3]);
            publishTimeMs = int.Parse(args[4]);
            watchTimeSec = int.Parse(args[5]);
            behaviour = int.Parse(args[6]);
        }
        else
        {
            Console.WriteLine("Error starting the program. Invalid arguments.\n\nUsage:\n-linux:\ncf_mqtt_broker_cluster_resilience_test_tool <broker_ip> <port> <topic_prefix> <nmr_of_clients> <publish_time_ms> <run_time_s> <behaviour>\n\n-windows:\ncf_mqtt_broker_cluster_resilience_test_tool.exe <broker_ip> <port> <topic_prefix> <nmr_of_clients> <publish_time_ms> <run_time_s> <behaviour>");
            Console.WriteLine("\n\nPress any key to close");
            Console.ReadKey();
            return;
        }

        Console.WriteLine();
        ConsoleWrite($"broker ip: {broker}:{port}, topic: {topicPrefix}, {clientCount} Clients\nRunning...");

        var cts = new CancellationTokenSource();
        var elapsedSeconds = Stopwatch.StartNew();
        try
        {
            Task clientSubscriber = Task.Run(() => StartSubscriber(broker, port, clientCount, topicPrefix, publishTimeMs, behaviour, cts));
            Console.WriteLine();
            var timer = new PeriodicTimer(TimeSpan.FromSeconds(watchTimeSec));
            while (await timer.WaitForNextTickAsync(cts.Token))
            {
                cts.Cancel();
                break;
            }

            elapsedSeconds.Stop();
            Console.WriteLine();

            if (SequenceErrorCount > 0)
                ConsoleWrite($"Finished with a total of {SequenceErrorCount} payload loss. Elapsed time: {elapsedSeconds.Elapsed}\n");

            else
                ConsoleWrite($"Finished with no payload lost. Elapsed time: {elapsedSeconds.Elapsed}\n");
        }
        catch (Exception)
        {
            ConsoleWrite($"Disconnecting clients...\n");
        }
        finally
        {
            Console.WriteLine("Press any key to close");
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

    private static int GetValidBehaviourInput(string prompt)
    {
        int value;
        do
        {
            Console.Write(prompt);
            string input = Console.ReadLine();
            if (int.TryParse(input, out value) && (value == 1 || value == 2))
                return value;

            Console.WriteLine($"Invalid value. Please enter a valid behaviour option.\n");
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
            // ConsoleWrite($"Publisher {clientId} connected.");

            string topic = $"{topicPrefix}{clientId}";

            int i = 0;
            while (!cancellationToken.IsCancellationRequested)
            {
                int payload = i % 10; /* 0 - 9 */

                // /* testing the subscribers response to errors */
                // if (i == 2)
                //     payload = i + 2;

                // if (clientId == "3" && (i == 83 || i == 56))
                //     payload = i + 1;

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
            ConsoleWrite($"Error: {ex.Source} - {ex.Message}");
        }
        finally
        {
            await mqttClient.DisconnectAsync();
            // Console.WriteLine($"Publisher {clientId} disconnected.");
        }
    }

    #endregion

    #region subscriber

    private static async Task StartSubscriber(string broker, int port, int clientCount, string topicPrefix, int publishTimeMs, int behaviour, CancellationTokenSource cts)
    {
        var options = new MqttClientOptionsBuilder()
            .WithTcpServer(broker, port)
            .Build();

        var mqttClient = new MqttFactory().CreateMqttClient();

        mqttClient.ConnectedAsync += async e =>
        {
            // ConsoleWrite($"Subscriber connected.");
            var mqttFactory = new MqttFactory();
            var subsOptionsBuilder = mqttFactory.CreateSubscribeOptionsBuilder();
            // Subscribe to all publisher topics
            for (int i = 1; i <= clientCount; i++)
            {
                subsOptionsBuilder.WithTopicFilter($"{topicPrefix}{i}");
            }
            var mqttSubscribeOptions = subsOptionsBuilder.Build();

            await mqttClient.SubscribeAsync(mqttSubscribeOptions);
            // ConsoleWrite($"Subscriber subscribed to {clientCount} topics.");
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
                       Interlocked.Increment(ref SequenceErrorCount);
                       //    ConsoleWrite($"Sequence error nº{SequenceErrorCount}: {topic}: Previous = {lastValues.Previous}, Actual = {lastValues.Actual}");

                       if (behaviour == 1)
                       {
                           ConsoleWrite($"Sequence error detected: {topic}: Previous = {lastValues.Previous}, Actual = {lastValues.Actual}");
                           cts.Cancel();
                       }

                   }
                   LastValues[topic] = (lastValues.Actual, value);
               }
           }
           catch (FormatException)
           {
               ConsoleWrite($"Non-integer value received on {topic}: {payload}");
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

    private static void ConsoleWrite(string message)
    {
        Console.WriteLine($"[{DateTime.Now.Hour}:{DateTime.Now.Minute}:{DateTime.Now.Second}]: {message}");
    }
}