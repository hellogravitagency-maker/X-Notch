# Todo Peek

`Todo Peek` adds a compact task panel to X-Notch's expanded view without adding anything to the reduced notch.

## What it does

- Shows up to `maxTasks` tasks in the expanded notch
- Lets you mark tasks complete with one click
- Adapts to light and dark themes
- Provides a vertical-friendly layout for side docking
- Supports:
  - `Todoist` with a personal API token
  - `Microsoft To Do` with a manually supplied Microsoft Graph access token

## Setup

The easiest setup path is now:

1. Install `Todo Peek`
2. Open `Plugin Manager`
3. Click `Set Up` on the Todo Peek card
4. Fill in the provider fields and press `Apply Configuration`

When the plugin starts for the first time, it also creates:

`%AppData%\X-Notch\Plugins\com.X-Notch.todopeek\settings.json`

Default file:

```json
{
  "provider": "Todoist",
  "maxTasks": 5,
  "refreshMinutes": 5,
  "todoist": {
    "apiToken": "",
    "filter": "(today | overdue | no date) & !subtask"
  },
  "microsoftTodo": {
    "accessToken": "",
    "listName": "Tasks"
  }
}
```

## Provider notes

### Todoist

Paste your personal API token into the `API token` field in Plugin Manager.

### Microsoft To Do

Paste a Microsoft Graph access token with `Tasks.Read` or `Tasks.ReadWrite` into the `Access token` field in Plugin Manager.

You can still edit `settings.json` manually if you want advanced control, but it is no longer required for normal setup.
