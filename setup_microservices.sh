#!/bin/bash

# Unzip the microservices archive
unzip TheWatch-Microservices-master.zip

# Move the contents to the TheWatch.Microservices directory
mv TheWatch-Microservices-master/* TheWatch.Microservices/

# Remove the now-empty directory
rmdir TheWatch-Microservices-master

# Navigate to the microservices directory
cd TheWatch.Microservices

# Restore dotnet dependencies
dotnet restore TheWatch.sln
