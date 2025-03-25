#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <unistd.h>
#include <arpa/inet.h>
#include <sys/select.h>
#include <errno.h>
#include <fcntl.h>

#define MAX_PORT 65535
#define MIN_PORT 1024
#define TIMEOUT_SEC 0    // 0 seconds for select()
#define TIMEOUT_USEC 10000  // 10ms timeout in microseconds

// Function to send handshake to check if ADB is available on the port
int is_adb_device(const char *host, int port) {
    int sock;
    struct sockaddr_in server;
    unsigned char handshake[] = {
        0x43, 0x4e, 0x58, 0x4e,  // CNXN
        0x01, 0x00, 0x00, 0x10,  // Version
        0x00, 0x00, 0x00, 0x00,  // Max data
        0x00, 0x00, 0x00, 0x00,  // Payload length
        0x00, 0x00, 0x00, 0x00,  // Checksum
        0xbc, 0xb1, 0xa7, 0xb1   // Magic
    };

    sock = socket(AF_INET, SOCK_STREAM, 0);  // Use TCP/IP
    if (sock == -1) {
        return 0;
    }

    server.sin_family = AF_INET;
    server.sin_port = htons(port);
    server.sin_addr.s_addr = inet_addr(host);

    // Set the socket to non-blocking for timeout control
    fcntl(sock, F_SETFL, O_NONBLOCK);

    // Try to connect with timeout
    if (connect(sock, (struct sockaddr *)&server, sizeof(server)) < 0) {
        if (errno == EINPROGRESS) {
            // Use select to wait for the connection to complete or timeout
            fd_set write_fds;
            struct timeval timeout;
            FD_ZERO(&write_fds);
            FD_SET(sock, &write_fds);

            timeout.tv_sec = TIMEOUT_SEC;
            timeout.tv_usec = TIMEOUT_USEC;

            int ret = select(sock + 1, NULL, &write_fds, NULL, &timeout);
            if (ret <= 0) {
                close(sock);
                return 0; // Timeout or error
            }
        } else {
            close(sock);
            return 0; // Connection failed
        }
    }

    // Send the handshake message
    if (send(sock, handshake, sizeof(handshake), 0) < 0) {
        close(sock);
        return 0;
    }

    // Receive response with timeout
    unsigned char response[24];
    fd_set read_fds;
    struct timeval timeout;
    FD_ZERO(&read_fds);
    FD_SET(sock, &read_fds);

    timeout.tv_sec = TIMEOUT_SEC;
    timeout.tv_usec = TIMEOUT_USEC;

    int ret = select(sock + 1, &read_fds, NULL, NULL, &timeout);
    if (ret <= 0) {
        close(sock);
        return 0; // Timeout or error
    }

    int bytes_received = recv(sock, response, sizeof(response), 0);
    if (bytes_received < 24) {
        close(sock);
        return 0;
    }

    // Check for valid ADB responses
    const unsigned char valid_responses[][4] = {
		{0x53, 0x59, 0x4e, 0x43}, // SYNC
		{0x43, 0x4c, 0x53, 0x45}, // CLSE
		{0x57, 0x52, 0x54, 0x45}, // WRTE
		{0x41, 0x55, 0x54, 0x48}, // AUTH
		{0x4f, 0x50, 0x45, 0x4e}, // OPEN
		{0x43, 0x4e, 0x58, 0x4e}, // CNXN
		{0x53, 0x54, 0x4c, 0x53}, // STLS
		{0x4f, 0x4b, 0x41, 0x59}  // OKAY
    };

    for (int i = 0; i < 8; i++) {
        if (memcmp(response, valid_responses[i], 4) == 0) {
            close(sock);
            return 1;  // Found valid response
        }
    }

    close(sock);
    return 0; // No valid response
}

// Function to check if a port is in use on loopback (127.0.0.1) for TCP/IP
int is_port_in_use(int port) {
    int sock;
    struct sockaddr_in server;

    sock = socket(AF_INET, SOCK_STREAM, 0);  // Use TCP/IP
    if (sock == -1) {
        return 0;
    }

    server.sin_family = AF_INET;
    server.sin_port = htons(port);
    server.sin_addr.s_addr = inet_addr("127.0.0.1"); // Bind only to loopback address

    // Set the socket to non-blocking for timeout control
    fcntl(sock, F_SETFL, O_NONBLOCK);

    // Try to bind to the loopback port for TCP/IP
    if (bind(sock, (struct sockaddr *)&server, sizeof(server)) < 0) {
        close(sock);
        return 1; // Port is in use
    }

    // Port is free, close the socket
    close(sock);
    return 0;
}

int main() {
    const char *host = "127.0.0.1";  // Loopback address
    
    // Iterate through the port range and check if ADB is listening
    for (int port = MIN_PORT; port <= MAX_PORT; port++) {
        if (is_port_in_use(port)) {
            if (is_adb_device(host, port)) {
                printf("%d\n", port);
            }
        }
    }

    return 0;
}
