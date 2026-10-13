import { CommonModule, CurrencyPipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { ReportSummary } from '../models/report.models';
import { ReportService } from '../services/report.service';
@Component({selector:'app-reports-page',standalone:true,imports:[CommonModule,CurrencyPipe,ReactiveFormsModule],templateUrl:'./reports-page.component.html',styleUrl:'./reports-page.component.scss'})
export class ReportsPageComponent { private readonly fb=inject(FormBuilder);private readonly reports=inject(ReportService);readonly summary=signal<ReportSummary|null>(null);readonly loading=signal(true);readonly error=signal<string|null>(null);readonly form=this.fb.nonNullable.group({fromDate:[''],toDate:['']});constructor(){this.load();}load():void{const value=this.form.getRawValue();this.loading.set(true);this.error.set(null);this.reports.getMine(value.fromDate,value.toDate).subscribe({next:summary=>{this.summary.set(summary);this.loading.set(false);},error:error=>{this.error.set(error.error?.detail??'Impossible de charger les rapports.');this.loading.set(false);}});} }
