SMTPBridge
==========

C# SMTP Bridge

This is code that I found on CodeProject and that I've adapted for my needs.

The core business need is that I wanted to have a foundation to create an SMTP server that could receive emails and route them to an application depending on basic rules.

The basic demo will simply receive the raw messages and place them in individual text files under \USER\AppData\Temp. You can implement a new processor that can route messages to a service bus for processing (which is what I did) in order to reduce the load off the SMTP server as much as possible. Let some enterprise bus component figure out where it needs to go and send it off. You could also directly create your own message processor that calls the API of your application if you wish - that will also work.