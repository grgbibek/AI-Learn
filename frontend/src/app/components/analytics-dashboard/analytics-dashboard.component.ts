import { Component, ElementRef, OnInit, ViewChild, inject, effect } from '@angular/core';
import { CommonModule } from '@angular/common';
import { AnalyticsService } from '../../services/analytics.service';
import { ClientAiService } from '../../services/client-ai.service';
import { Chart, registerables } from 'chart.js';

Chart.register(...registerables);

@Component({
  selector: 'app-analytics-dashboard',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './analytics-dashboard.component.html',
  styleUrl: './analytics-dashboard.component.css'
})
export class AnalyticsDashboardComponent implements OnInit {
  analytics = inject(AnalyticsService);
  clientAi = inject(ClientAiService);

  @ViewChild('statusChartCanvas') statusCanvas!: ElementRef<HTMLCanvasElement>;
  @ViewChild('priorityChartCanvas') priorityCanvas!: ElementRef<HTMLCanvasElement>;
  @ViewChild('agentChartCanvas') agentCanvas!: ElementRef<HTMLCanvasElement>;

  private statusChart: Chart | null = null;
  private priorityChart: Chart | null = null;
  private agentChart: Chart | null = null;

  constructor() {
    effect(() => {
      const data = this.analytics.metrics();
      if (data) {
        setTimeout(() => this.renderCharts(data), 50);
      }
    });
  }

  ngOnInit(): void {
    this.analytics.loadMetrics();
  }

  refreshMetrics(): void {
    this.analytics.loadMetrics();
  }

  private renderCharts(data: any): void {
    if (!this.statusCanvas || !this.priorityCanvas || !this.agentCanvas) return;

    // Destory existing chart instances if any
    this.statusChart?.destroy();
    this.priorityChart?.destroy();
    this.agentChart?.destroy();

    // 1. Status Chart (Doughnut)
    this.statusChart = new Chart(this.statusCanvas.nativeElement, {
      type: 'doughnut',
      data: {
        labels: ['Todo', 'In Progress', 'Done'],
        datasets: [{
          data: [data.statusDistribution.todo, data.statusDistribution.inProgress, data.statusDistribution.done],
          backgroundColor: ['#64748b', '#3b82f6', '#10b981'],
          borderColor: '#1e293b',
          borderWidth: 2
        }]
      },
      options: {
        responsive: true,
        plugins: {
          legend: { labels: { color: '#cbd5e1' } }
        }
      }
    });

    // 2. Priority Chart (Bar)
    this.priorityChart = new Chart(this.priorityCanvas.nativeElement, {
      type: 'bar',
      data: {
        labels: ['Low', 'Medium', 'High', 'Critical'],
        datasets: [{
          label: 'Task Count',
          data: [
            data.priorityDistribution.low,
            data.priorityDistribution.medium,
            data.priorityDistribution.high,
            data.priorityDistribution.critical
          ],
          backgroundColor: ['#38bdf8', '#f59e0b', '#f97316', '#ef4444'],
          borderRadius: 6
        }]
      },
      options: {
        responsive: true,
        scales: {
          x: { ticks: { color: '#cbd5e1' }, grid: { color: '#334155' } },
          y: { ticks: { color: '#cbd5e1' }, grid: { color: '#334155' }, beginAtZero: true }
        },
        plugins: {
          legend: { display: false }
        }
      }
    });

    // 3. Agent Pipeline Audit Chart (Pie)
    this.agentChart = new Chart(this.agentCanvas.nativeElement, {
      type: 'pie',
      data: {
        labels: ['Approved', 'Rejected'],
        datasets: [{
          data: [data.agentMetrics.approvedRuns, data.agentMetrics.rejectedRuns],
          backgroundColor: ['#10b981', '#ef4444'],
          borderColor: '#1e293b',
          borderWidth: 2
        }]
      },
      options: {
        responsive: true,
        plugins: {
          legend: { labels: { color: '#cbd5e1' } }
        }
      }
    });
  }
}
