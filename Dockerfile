# Set the base image to use for the container
FROM mcr.microsoft.com/dotnet/core/runtime:3.1

# Set the working directory inside the container
WORKDIR /app

# Copy the compiled application files to the container
COPY . .

# Set the entry point for the container
ENTRYPOINT ["dotnet", "SkillzBot.dll"]