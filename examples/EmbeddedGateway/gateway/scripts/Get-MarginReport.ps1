param($Request)

# Simulate ERP margin data extraction.
# In production this would call OLEDB, an ERP API, or SQL via Managed Identity.

$margins = @(
    [PSCustomObject]@{ product = "Widget A";    category = "Hardware"; revenue = 142000; cogs = 82360;  margin = 0.42; trend = "up"   }
    [PSCustomObject]@{ product = "Widget B";    category = "Hardware"; revenue = 57000;  cogs = 39330;  margin = 0.31; trend = "flat" }
    [PSCustomObject]@{ product = "Gadget X";    category = "Software"; revenue = 98000;  cogs = 39200;  margin = 0.60; trend = "up"   }
    [PSCustomObject]@{ product = "Gadget Y Pro";category = "Software"; revenue = 210000; cogs = 105000; margin = 0.50; trend = "down" }
)

$summary = [PSCustomObject]@{
    generatedAt    = (Get-Date).ToString("o")
    reportingPeriod = "Q2-2026"
    currency       = "USD"
    margins        = $margins
    averageMargin  = [math]::Round(($margins | Measure-Object -Property margin -Average).Average, 3)
}

$summary | ConvertTo-Json -Depth 5
