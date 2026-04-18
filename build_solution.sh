#!/bin/bash

export PATH="$PATH:/ephemeral/nix/store/1k3kpkwdb9qrq9481l9wkwk7y4mvd7c5-dotnet-sdk-wrapped-9.0.203/bin"
dotnet build TheWatch.Microservices/TheWatch.sln
