# Coreflux Cluster Resillience Test Tool

### Description

The Coreflux Cluster Resillience Test Tool provides an easy way to check for payload losses, when using the cluster. <br>
<br>
This tool creates a user input amount of clients that connect to a cluster broker that then send the payload from 0-9 in user input intervals. An extra client, that subscribes to the topics the payload is being sent to from the publishers, determines if the sequence received is valid, testing the cluster's resillience.

### Prerequisites
- Cluster IP Address;

### Setup instructions

1. Go to the Releases tab;
2. Download the zip file for your OS;
3. Extract the contents;
4. Run the executable file.

### Running the tool

The user will be prompt by the console to introduce the broker's ip address, the port, the topic prefix, the number of clients, the publish time in ms, the run time and the program's behaviour:
```
Broker Address: 
Port:
Topic Prefix:
Number of clients:
Publish time (ms):
Run time (s): 
Select a behaviour option:

(1) Stop at the first missed payload
(2) Count the missed payload

Option:
```


|Name|Description| Example|
|-|-|-|
|Address| IP address or hostname of the *MQTT* cluster.| `127.0.0.1`|
|Port|Port number on which the *MQTT* cluster is running. | `1883` |
|Topic Prefix | The prefix of the topic the clients will be sending / receiving the payload| `mqtt/` |
|Number of clients|The number of clients that are going to connect and send the payload to the cluster| `10`|
|Publish time|The time between each payload sent to the cluster, in milliseconds | `100`|
|Run time |The time the program will run for, in seconds|`60`|
|Behaviour|If the program should stop when it detects the first missed payload or if it should run and count all the instances it happens|  `2` |


### Output

#### Running
When the tool starts, it will show when the subscriber connects and subscribes to the topics as the publisher clients connect to the broker cluster.
*Example*:
```
Subscriber connected.
Subscriber subscribed to <number_of_clients> topics.
Publisher 7 connected.
Publisher 2 connected.
publisher 8 connected.
...
```

Each topic represents a client, and the subscriber will subscribe to all topics and monitorise the changes in the 

#### Failure

If the subscriber detects a miss on the sequence on one topic, you'll get the following message:

```
Sequence error on <topic>: Previous = <previous_value>, Actual = <actual_value>
```
where *topic* is the topic the subscriber detected the miss, *previous_value* the last value received and *actual_value* the value that skipped the sequence.

If the selected behaviour was 1, then the program will stop, disconnect all the clients and prompt the user to close:

```
Disconnecting clients...

Press any key to close
```

If there's an exception different than a miss on the sequence, you'll get the message:

```
Error: <error_information>
```

#### After run time
If no sequencial payload was lost, you'll get the following message:

```
Finished with no payload lost. Elapsed time: <elapsed_time>
```

If the behaviour selected was the option 2 after the run time has ended, you'll get the following message:

```
Finished with a total of <number_of_misses> payload loss. Elapsed time: <elapsed_time>
```
where *number_of_misses* the count of the payloads that were skipped and *elapsed_time* is the time the program ran for.

To close the program you'll be prompted to press any key of the keyboard:

```
Press any key to close
```