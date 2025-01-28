# Coreflux Cluster Resillience Test Tool

### Description

The Coreflux Cluster Resillience Test Tool provides an easy way to check for payload losses, when using the cluster. <br>
<br>
This tool creates a user input amount of clients that connect to a cluster broker that then send the payload from 0-9 in user input intervals. An extra client, that subscribes to the topics the payload is being sent to from the publishers, determines if the sequence received is valid, testing the cluster's resilience.

### Prerequisites
- Cluster IP Address;

### Setup instructions

1. Go to the Releases tab;
2. Download the zip file for your OS;
3. Extract the contents;
4. Run the executable file.

### Running the tool

The user will be prompt by the console to introduce the broker's ip address, the port, the topic prefix, the number of clients, the publish time in ms and the run time:
```
Broker Address: 
Port:
Topic Prefix:
Number of clients:
Publish time (ms):
Run time (s): 
```


|Name|Description| Example|
|-|-|-|
|Address| IP address or hostname of the *MQTT* cluster.| `127.0.0.1`|
|Port|Port number on which the *MQTT* cluster is running. | `1883` |
|Topic Prefix | The prefix of the topic the clients will be sending / receiving the payload| `mqtt/` |
|Number of clients|The number of clients that are going to connect and send the payload to the cluster| `10`|
|Publish time|The time between each payload to be sent to the cluster, in milliseconds | `100`|
|Run time |The time the program will run for, in seconds|`60`|



### Output

#### Running
When the tool starts, it will write to the console the topics the subscriber subscribes to and when the publisher clients connect.

*Example*:
```
Subscriber connected.
Subscriber subscribed to mqtt/1
Subscriber subscribed to mqtt/2
Subscriber subscribed to mqtt/3
...

Publisher 7 connected.
Publisher 2 connected.
publisher 8 connected.
...
```

#### Success
If the subscriber doesn't detect a miss on the sequence after the run time has ended, you'll get the following message:

```
Finished with no payload loss. Elapsed time: <elapsed_time>
```
where *elapsed_time* is the time the program ran for.

#### Failure

If the subscriber detects a miss on the sequence on one topic, you'll get the following message:

```
Sequence error on <topic>: Previous = <previous_value>, Actual = <actual_value>
```
where *topic* is the topic the subscriber detected the miss, *previous value* the last value received and *actual value* the value that skipped the sequence.


If there's an exception different than a miss on the sequence, you'll get the message:

```
Error: <error_information>
```