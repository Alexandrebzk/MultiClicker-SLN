# Multi-Clicker Solution

This solution is a Windows Forms application that provides a user interface for managing and interacting with multiple windows of a specific application.

## Key Features

- **Window Management**: The application can find and interact with windows of a specific application. It can simulate clicks, double clicks and key strokes on these windows.


## Key Components

### MultiClicker.cs

This is the main form of the application. It handles the loading of the form and the generation of the user interface. It also handles the rearrangement of panels according to the configuration file.

### WindowManagement.cs

This class provides methods for finding and interacting with windows. It can simulate clicks and double clicks on these windows.

### ConfigManagement.cs

This class provides methods for loading and saving a configuration file. The configuration file is a JSON file that stores information about the panels and their order, as well as the key bindings.

### PanelManagement.cs

This class provides methods for setting the background image of a panel.

## Usage

When the application is started, it finds all windows of a specific application and creates a panel for each window. The panels can be rearranged according to the configuration file.

Each panel has a context menu that allows the user to move the panel up or down, or change its background image.

The application also provides a help button that displays a tooltip with a table of key bindings when hovered over.

The application can be closed by clicking the close button.

## Configuration

The configuration of the application is stored in a JSON file named `config.json`. This file stores information about the panels and their order, as well as the key bindings.

The configuration file can be updated by rearranging the panels in the user interface and then saving the configuration.

## Dependencies

The application uses the Newtonsoft.Json library for serializing and deserializing the configuration file.

## Future Work

Future work could include adding customizable key bindings, better UI and so on.
## Building the Application

To build the application, you will need Visual Studio installed on your machine. Follow the steps below:

2. **Open the solution in Visual Studio**: Navigate to the directory where you cloned the repository and open the `.sln` file in Visual Studio.

3. **Restore NuGet Packages**: Right-click on the solution in the Solution Explorer and select "Restore NuGet Packages" to download and install any necessary dependencies.

4. **Build the solution**: You can build the solution by clicking on "Build" in the menu, and then "Build Solution". This will compile the code and create an executable file.

5. **Run the application**: After building the solution, you can run the application by clicking on "Debug" in the menu, and then "Start Debugging". This will start the application and open the main window.