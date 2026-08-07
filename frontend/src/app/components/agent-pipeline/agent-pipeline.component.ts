import { Component, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { AgentPipelineService } from '../../services/agent-pipeline.service';

@Component({
  selector: 'app-agent-pipeline',
  standalone: true,
  imports: [FormsModule, DatePipe],
  templateUrl: './agent-pipeline.component.html',
  styleUrl: './agent-pipeline.component.css'
})
export class AgentPipelineComponent {
  readonly agents = inject(AgentPipelineService);

  featureRequest = signal<string>('');
  showAuditLog = signal<boolean>(false);

  onPlan(): void {
    const request = this.featureRequest().trim();
    if (!request) {
      return;
    }
    this.agents.planFeature(request);
  }

  onToggleAuditLog(): void {
    const next = !this.showAuditLog();
    this.showAuditLog.set(next);
    if (next) {
      this.agents.loadAuditLog();
    }
  }
}
