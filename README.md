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

## Key Binds
### KB related events
- SELECT_NEXT --> Next character
- SELECT_PREVIOUS --> Previous character
- HAVENBAG --> All in havenbag
- GROUP_INVITE --> Invite all characters
- DOFUS_HAVENBAG --> Dofus havenbag hotkey
- DOFUS_OPEN_DISCUSSION --> Dofus open discussion hotkey
- TRAVEL --> 
### Key Binds values
- LButton --> 1
- RButton --> 2
- Cancel --> 3
- MButton --> 4
- XButton1 --> 5
- XButton2 --> 6
- Back --> 8
- Tab --> 9
- LineFeed --> 10
- Clear --> 12
- Return --> 13
- Return --> 13
- ShiftKey --> 16
- ControlKey --> 17
- Menu --> 18
- Pause --> 19
- Capital --> 20
- Capital --> 20
- KanaMode --> 21
- KanaMode --> 21
- KanaMode --> 21
- JunjaMode --> 23
- FinalMode --> 24
- HanjaMode --> 25
- HanjaMode --> 25
- Escape --> 27
- IMEConvert --> 28
- IMENonconvert --> 29
- IMEAceept --> 30
- IMEAceept --> 30
- IMEModeChange --> 31
- Space --> 32
- PageUp --> 33
- PageUp --> 33
- Next --> 34
- Next --> 34
- End --> 35
- Home --> 36
- Left --> 37
- Up --> 38
- Right --> 39
- Down --> 40
- Select --> 41
- Print --> 42
- Execute --> 43
- PrintScreen --> 44
- PrintScreen --> 44
- Insert --> 45
- Delete --> 46
- Help --> 47
- D0 --> 48
- D1 --> 49
- D2 --> 50
- D3 --> 51
- D4 --> 52
- D5 --> 53
- D6 --> 54
- D7 --> 55
- D8 --> 56
- D9 --> 57
- A --> 65
- B --> 66
- C --> 67
- D --> 68
- E --> 69
- F --> 70
- G --> 71
- H --> 72
- I --> 73
- J --> 74
- K --> 75
- L --> 76
- M --> 77
- N --> 78
- O --> 79
- P --> 80
- Q --> 81
- R --> 82
- S --> 83
- T --> 84
- U --> 85
- V --> 86
- W --> 87
- X --> 88
- Y --> 89
- Z --> 90
- LWin --> 91
- RWin --> 92
- Apps --> 93
- Sleep --> 95
- NumPad0 --> 96
- NumPad1 --> 97
- NumPad2 --> 98
- NumPad3 --> 99
- NumPad4 --> 100
- NumPad5 --> 101
- NumPad6 --> 102
- NumPad7 --> 103
- NumPad8 --> 104
- NumPad9 --> 105
- Multiply --> 106
- Add --> 107
- Separator --> 108
- Subtract --> 109
- Decimal --> 110
- Divide --> 111
- F1 --> 112
- F2 --> 113
- F3 --> 114
- F4 --> 115
- F5 --> 116
- F6 --> 117
- F7 --> 118
- F8 --> 119
- F9 --> 120
- F10 --> 121
- F11 --> 122
- F12 --> 123
- F13 --> 124
- F14 --> 125
- F15 --> 126
- F16 --> 127
- F17 --> 128
- F18 --> 129
- F19 --> 130
- F20 --> 131
- F21 --> 132
- F22 --> 133
- F23 --> 134
- F24 --> 135
- NumLock --> 144
- Scroll --> 145
- LShiftKey --> 160
- RShiftKey --> 161
- LControlKey --> 162
- RControlKey --> 163
- LAlt --> 164
- RAlt --> 165