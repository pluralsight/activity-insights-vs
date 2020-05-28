# Pluralsight Activity Insights

Visual Studio extension for Pluralsight Activity Insights, interfacing with the [Activity Insights CLI](https://github.com/ps-dev/activity-insights-cli).
It populates your coding activity on the [Activity Insights Dashboard](https://app.pluralsight.com/activity-insights-beta) so you
can better understand your day.

Currently supports Visual Studio 2019 and 2017.

## Installation

### From Visual Studio
  1. Go to Tools > Extensions
  2. In the online tab, search for "Pluralsight Activity Insights"
  3. Click install

### Directly from VSIX
  1. Download the latest [release](https://github.com/ps-dev/activity-insights-vs/releases)
  2. Find the downloaded .vsix file, double click it and follow the prompts
  
  
## Registration
  1. Once your extension is installed, you should be prompted to register your device
  2. Click `OK` and you should be redirected to your browser
  3. That's it! You should start to see your coding activity in the [dashboard](https://app.pluralsight.com/activity-insights-beta) almost immediately.
  
  
## Troubleshooting
  1. You can view logs related to Visual Studio errors in `$HOME_DIR/.pluralsight/vs-extension.logs` and logs related to CLI errors in `$HOME_DIR/.pluralsight/activity-insights.logs`
  2. Please file an issue and include relevant logs
